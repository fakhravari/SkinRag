using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

internal static class LiveChecks
{
    public static async Task RunAsync(string baseUrl)
    {
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(240)
        };
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Live check failed: " + message);
            }

            checks++;
        }

        async Task<JsonElement> Get(string path)
        {
            using var response = await http.GetAsync(path);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        }

        async Task<JsonElement> Ask(object body)
        {
            var timer = Stopwatch.StartNew();
            using var response = await http.PostAsJsonAsync("/api/consultation/ask", body);
            var text = await response.Content.ReadAsStringAsync();
            Check(response.IsSuccessStatusCode, $"Consultation HTTP {(int)response.StatusCode}: {text}");
            var result = JsonSerializer.Deserialize<JsonElement>(text);
            Console.WriteLine($"Live consultation: {result.GetProperty("intent")}, {result.GetProperty("responseMode")}, {result.GetProperty("products").GetArrayLength()} products, {timer.Elapsed.TotalSeconds:F1}s");
            return result;
        }

        var greeting = await Ask(new { question = "سلام" });
        Check(
            greeting.GetProperty("intent").GetString() == "GREETING"
            && greeting.GetProperty("retrievalMethod").GetString() == "none",
            "Greeting did not bypass retrieval");
        var smallTalk = await Ask(new { question = "سلام رفیق خوبی؟" });
        Check(smallTalk.GetProperty("intent").GetString() == "SMALL_TALK"
            && smallTalk.GetProperty("conversationTopic").GetString() == "Wellbeing"
            && smallTalk.GetProperty("retrievalMethod").GetString() == "none",
            "Small talk did not bypass retrieval");
        var joke = await Ask(new { message = "یه جوک درباره کرم بگو 😂" });
        Check(
            joke.GetProperty("intent").GetString() == "OFF_TOPIC"
            && joke.GetProperty("products").GetArrayLength() == 0,
            "Joke was treated as product search");
        using (var invalid = await http.PostAsJsonAsync("/api/consultation/ask", new { question = new string('ه', 30) }))
        {
            Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Spam was accepted");
        }

        using (var invalid = await http.PostAsJsonAsync("/api/consultation/ask", new
        {
            question = "کرم",
            message = "شامپو"
        }))
        {
            Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Contradictory request aliases were accepted");
        }

        var taxonomy = await Get("/api/catalog/filters");
        Check(taxonomy.GetProperty("categories").GetArrayLength() == 48, "Categories changed");
        Check(
            taxonomy.GetProperty("brands").EnumerateArray().All(b => !b.GetProperty("name").GetString()!.Contains("آزمایشی")),
            "Test brand wording returned");
        var hair = await Get("/api/catalog/products?domain=hair&hairType=hair-curly&maxPrice=1500000&pageSize=100");
        Check(hair.GetProperty("total").GetInt32() > 0, "Hair filter returned no data");
        Check(
            hair.GetProperty("items").EnumerateArray().All(p => p.GetProperty("variants").EnumerateArray().All(v => v.GetProperty("stockQuantity").GetInt32() > 0 && v.GetProperty("price").GetDecimal() <= 1500000)),
            "Budget/stock did not match the same variant");
        var unavailable = await Get("/api/catalog/products?search=DEMO-SKIN-01-05&maxPrice=700000");
        Check(unavailable.GetProperty("total").GetInt32() == 0, "Sold-out cheaper variant was eligible");
        using (var invalid = await http.GetAsync("/api/catalog/products?skinType=unknown"))
        {
            Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Unknown profile accepted");
        }

        var wait = Stopwatch.StartNew();
        while (true)
        {
            var status = await Get("/api/knowledge/status");
            if (status.GetProperty("isReady").GetBoolean())
            {
                break;
            }

            if (wait.Elapsed > TimeSpan.FromMinutes(12))
            {
                throw new TimeoutException("Knowledge index did not become ready");
            }

            Console.WriteLine($"Waiting for index: {status.GetProperty("processedProducts")}/{status.GetProperty("totalProducts")}");
            await Task.Delay(TimeSpan.FromSeconds(15));
        }

        var skin = await Ask(new
        {
            question = "سلام، پوستم خشکه، یک کرم مرطوب کننده ارزان میخوام",
            maxPrice = 2000000
        });
        Check(
            skin.GetProperty("intent").GetString() is "PRODUCT_SEARCH" or "SKIN_CONSULTATION",
            "Skin request misclassified");
        Check(skin.GetProperty("products").GetArrayLength() > 0, "Skin search returned no products");
        Check(
            skin.GetProperty("products").EnumerateArray().All(m =>
            {
                var p = m.GetProperty("product");
                return p.GetProperty("domain").GetString() == "skin" && p.GetProperty("stockQuantity").GetInt32() > 0
                    && p.GetProperty("price").GetDecimal() <= 2000000;
            }),
            "Recommended product violated stock, domain or budget");
        var recommended = skin.GetProperty("products").EnumerateArray().Select(x => x.GetProperty("product").GetProperty("id").GetInt32())
            .ToHashSet();
        Check(
            skin.GetProperty("recommendations").EnumerateArray().All(r => recommended.Contains(r.GetProperty("productId").GetInt32())),
            "Recommendation not in validated products");
        var price = await Ask(new
        {
            question = "قیمتش چنده؟",
            conversationId = skin.GetProperty("conversationId").GetGuid()
        });
        Check(
            price.GetProperty("intent").GetString() == "PRICE_INQUIRY"
            && price.GetProperty("retrievalMethod").GetString() == "sql-product-reference",
            "Price follow-up lost product reference");
        Check(
            price.GetProperty("products").EnumerateArray().All(m => recommended.Contains(m.GetProperty("product").GetProperty("id").GetInt32())),
            "Price follow-up switched products");
        var hairReply = await Ask(new
        {
            question = "برای موی فر و وز، کرم مو بدون آبکشی میخوام",
            domain = "hair",
            hairType = "hair-curly",
            categorySlug = "leave-in"
        });
        Check(hairReply.GetProperty("products").GetArrayLength() > 0, "Hair consultation returned no products");
        Check(
            hairReply.GetProperty("products").EnumerateArray().All(m => m.GetProperty("product").GetProperty("categorySlug").GetString() == "leave-in"),
            "Model changed explicit category");
        var details = await Ask(new { question = "ترکیبات محصول #1 چیه؟" });
        Check(
            details.GetProperty("intent").GetString() == "PRODUCT_DETAILS"
            && details.GetProperty("products")[0].GetProperty("product").GetProperty("id").GetInt32() == 1,
            "Explicit details lookup failed");
        Check(
            details.GetProperty("retrievalMethod").GetString() == "sql-product-reference",
            "Explicit ID unnecessarily used vectors");
        Console.WriteLine($"{checks} live checks passed against SQL Server and Ollama.");
    }
}
