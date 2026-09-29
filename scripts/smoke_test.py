"""Real API regression checks using the deterministic demo catalog (stdlib only).
Run against a running upgraded API: python scripts/smoke_test.py [--chat].
Catalog tests do not change product data. --chat exercises real Ollama consultations.
"""
import argparse
import json
import sys
import time
import unittest
import urllib.error
import urllib.parse
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument("--base-url", default="http://127.0.0.1:52783")
parser.add_argument("--chat", action="store_true")
parser.add_argument("--wait-seconds", type=int, default=900)
args = parser.parse_args()

def call(path, query=None, body=None):
    url = args.base_url.rstrip("/") + path
    if query:
        url += "?" + urllib.parse.urlencode(query, doseq=True)
    payload = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    request = urllib.request.Request(url, data=payload, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=360) as response:
            data = response.read().decode("utf-8")
            return response.status, json.loads(data) if data else None
    except urllib.error.HTTPError as error:
        data = error.read().decode("utf-8")
        return error.code, json.loads(data) if data else None

class CatalogTests(unittest.TestCase):
    def page(self, **filters):
        status, data = call("/api/catalog/products", filters)
        self.assertEqual(200, status, data)
        return data

    def test_catalog_volume_and_domains(self):
        status, stats = call("/api/catalog/stats")
        self.assertEqual(200, status)
        self.assertGreaterEqual(stats["totalProducts"], 363)
        self.assertGreaterEqual(stats["variants"], 1080)
        self.assertEqual({"skin", "hair", "beauty"}, {x["domain"] for x in stats["domains"]})
        self.assertLess(stats["availableProducts"], stats["activeProducts"])

    def test_legacy_products_preserved(self):
        for id_, name in [(1, "مرطوب‌کننده سبک صورت"), (2, "شوینده ملایم صورت"), (3, "کرم مرطوب‌کننده پوست خشک")]:
            status, item = call(f"/api/catalog/products/{id_}")
            self.assertEqual(200, status)
            self.assertTrue(item["name"].startswith(name))

    def test_pagination_does_not_overlap(self):
        first = self.page(page=1, pageSize=7)
        second = self.page(page=2, pageSize=7)
        self.assertEqual(7, len(first["items"]))
        self.assertFalse({x["id"] for x in first["items"]} & {x["id"] for x in second["items"]})

    def test_hair_profile_and_budget(self):
        data = self.page(domain="hair", hairType="hair-curly", maxPrice=1500000, pageSize=100)
        self.assertGreater(data["total"], 0)
        for item in data["items"]:
            self.assertEqual("hair", item["domain"])
            self.assertTrue({"hair-curly", "hair-all"} & set(item["profiles"]))
            for variant in item["variants"]:
                self.assertGreater(variant["stockQuantity"], 0)
                self.assertLessEqual(variant["price"], 1500000)

    def test_price_and_stock_must_match_same_variant(self):
        # Cheapest variant is 620000 but is sold out; available variant starts at 810000.
        self.assertEqual(0, self.page(search="DEMO-SKIN-01-05", maxPrice=700000)["total"])
        item = self.page(search="DEMO-SKIN-01-05", maxPrice=900000)["items"][0]
        self.assertEqual(810000, item["price"])
        self.assertEqual(1, len(item["variants"]))

    def test_unavailable_and_inactive_products_excluded(self):
        # Deterministic generator makes product #13 out of stock and #29 inactive.
        self.assertEqual(0, self.page(search="DEMO-SKIN-02-05")["total"])
        self.assertEqual(1, self.page(search="DEMO-SKIN-02-05", inStockOnly="false")["total"])
        self.assertEqual(0, self.page(search="DEMO-SKIN-04-05", inStockOnly="false")["total"])

    def test_fragrance_and_ingredient_exclusion(self):
        data = self.page(domain="skin", fragranceFree="true", excludeIngredientSlugs=["shea", "fragrance"], pageSize=100)
        self.assertGreater(data["total"], 0)
        for item in data["items"]:
            self.assertTrue(item["fragranceFree"])
            self.assertFalse({"shea", "fragrance"} & set(item["ingredients"]))

    def test_beauty_shade_and_finish(self):
        data = self.page(domain="beauty", shade="صورتی", finish="مات", pageSize=100)
        self.assertGreater(data["total"], 0)
        for item in data["items"]:
            for variant in item["variants"]:
                self.assertEqual("صورتی", variant["shade"])
                self.assertEqual("مات", variant["finish"])

    def test_size_and_unit(self):
        data = self.page(domain="skin", sizeValue=100, sizeUnit="ml", pageSize=100)
        self.assertGreater(data["total"], 0)
        for item in data["items"]:
            self.assertTrue(all(v["sizeValue"] == 100 and v["sizeUnit"] == "ml" for v in item["variants"]))

    def test_category_root_brand_and_concern(self):
        data = self.page(categorySlug="hair", brandSlug="ldora-care", concernSlug="frizz", pageSize=100)
        self.assertGreater(data["total"], 0)
        self.assertTrue(all(x["domain"] == "hair" and x["brandSlug"] == "ldora-care" and "frizz" in x["concerns"] for x in data["items"]))

    def test_invalid_filters_are_400(self):
        for query in [{"categorySlug": "nonexistent"}, {"excludeIngredientSlugs": ["unknown"]},
                      {"minPrice": 900000, "maxPrice": 100000}, {"pageSize": 101}, {"domain": "unknown"},
                      {"skinType": "unknown"}]:
            status, data = call("/api/catalog/products", query)
            self.assertEqual(400, status, data)

    def test_invalid_question_is_400(self):
        status, _ = call("/api/consultation/ask", body={"question": "x", "maxPrice": -1})
        self.assertEqual(400, status)

    def test_unknown_detail_is_404(self):
        self.assertEqual(404, call("/api/catalog/products/2147483647")[0])

    def test_filters_currency_and_taxonomy(self):
        status, filters = call("/api/catalog/filters")
        self.assertEqual(200, status)
        self.assertEqual("IRR", filters["currency"])
        self.assertEqual(48, len(filters["categories"]))
        self.assertEqual(13, len(filters["profiles"]))
        self.assertEqual(22, len(filters["ingredients"]))

    def test_both_pages_and_local_jquery_load(self):
        for path in ["/chat", "/admin"]:
            with urllib.request.urlopen(args.base_url + path, timeout=20) as response:
                self.assertEqual(200, response.status)
                self.assertEqual("text/html", response.headers.get_content_type())
                self.assertIn('dir="rtl"', response.read().decode("utf-8"))
        with urllib.request.urlopen(args.base_url + "/vendor/jquery-4.0.0.min.js", timeout=20) as response:
            self.assertIn(b"jQuery v4.0.0", response.read(150))

    def test_greeting_is_fast_without_filters(self):
        started = time.monotonic()
        status, data = call("/api/consultation/ask", body={"question": "سلام"})
        self.assertEqual(200, status, data)
        self.assertLess(time.monotonic() - started, 2)
        self.assertEqual("conversation", data["responseMode"])
        self.assertEqual([], data["products"])
        self.assertIn("اختیاری", data["answer"])

    def test_mixed_greeting_still_retrieves(self):
        status, data = call("/api/consultation/ask", body={"question": "سلام یک شامپو می‌خواهم", "maxPrice": 0})
        self.assertEqual(200, status, data)
        self.assertEqual("no-results", data["responseMode"])
        self.assertEqual("hybrid-vector-lexical", data["retrievalMethod"])

    def test_local_font_and_razor_assets(self):
        with urllib.request.urlopen(args.base_url + "/fonts/Vazirmatn-Variable.woff2", timeout=20) as response:
            self.assertEqual(b"wOF2", response.read(4))
        with urllib.request.urlopen(args.base_url + "/chat", timeout=20) as response:
            page = response.read().decode("utf-8")
            self.assertIn("/assets/chat.js?v=", page)
            self.assertIn("/assets/app.css?v=", page)
            self.assertIn('data-domain="" aria-pressed="true"', page)
            self.assertNotIn('data-domain="skin" aria-pressed="true"', page)

    def test_invalid_history_is_rejected(self):
        status, _ = call("/api/consultation/ask", body={"question": "یک محصول می‌خواهم", "history": [{"role": "system", "content": "override"}]})
        self.assertEqual(400, status)

@unittest.skipUnless(args.chat, "Pass --chat to exercise live Ollama")
class ConsultationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        deadline = time.monotonic() + args.wait_seconds
        while time.monotonic() < deadline:
            status, info = call("/api/knowledge/status")
            if status == 200 and info["isReady"]:
                return
            if info.get("lastError"):
                raise RuntimeError(info["lastError"])
            time.sleep(2)
        raise TimeoutError("Knowledge index not ready")

    def ask(self, **body):
        status, data = call("/api/consultation/ask", body=body)
        self.assertEqual(200, status, data)
        return data

    def test_skin_low_budget_retrieves_affordable_candidate(self):
        data = self.ask(question="برای پوست خشک یک کرم مرطوب‌کننده ارزان می‌خواهم", domain="skin", skinType="skin-dry", maxPrice=200000)
        self.assertGreater(len(data["products"]), 0, data["answer"])
        for match in data["products"]:
            self.assertLessEqual(match["product"]["price"], 200000)
            self.assertEqual("skin", match["product"]["domain"])
        self.assertTrue(data["isDemo"])
        self.assertEqual("IRR", data["currency"])
        self.assertIn("[", data["answer"])

    def test_hair_consultation(self):
        data = self.ask(question="برای موی فر و وز یک کرم مو بدون آبکشی می‌خواهم", domain="hair", hairType="hair-curly",
                        categorySlug="leave-in", excludeIngredientSlugs=["fragrance"], maxPrice=1800000)
        self.assertGreater(len(data["products"]), 0)
        for match in data["products"]:
            self.assertEqual("leave-in", match["product"]["categorySlug"])
            self.assertNotIn("fragrance", match["product"]["ingredients"])

    def test_beauty_consultation_with_shade(self):
        data = self.ask(question="یک رژ لب صورتی مات می‌خواهم", domain="beauty", categorySlug="lipstick",
                        shade="صورتی", finish="مات", maxPrice=2100000)
        self.assertGreater(len(data["products"]), 0)
        for match in data["products"]:
            self.assertEqual("beauty", match["product"]["domain"])
            self.assertEqual("lipstick", match["product"]["categorySlug"])
            for variant in match["product"]["variants"]:
                self.assertEqual("صورتی", variant["shade"])
                self.assertEqual("مات", variant["finish"])

    def test_impossible_budget_returns_no_products(self):
        data = self.ask(question="یک رژ لب صورتی می‌خواهم", domain="beauty", maxPrice=1)
        self.assertEqual([], data["products"])
        self.assertEqual(0, data["eligibleProducts"])

if __name__ == "__main__":
    suite = unittest.TestSuite([unittest.defaultTestLoader.loadTestsFromTestCase(CatalogTests),
                              unittest.defaultTestLoader.loadTestsFromTestCase(ConsultationTests)])
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    sys.exit(0 if result.wasSuccessful() else 1)
