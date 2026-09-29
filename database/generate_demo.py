"""Deterministic, synthetic catalog. Generates JSON and a repeatable additive SQL seed.
No real brand, clinical efficacy, or real inventory is represented.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BRANDS = [
    ("roshana-demo", "روشانـا آزمایشی"),
    ("nilava-demo", "نیلاوا آزمایشی"),
    ("avisa-demo", "آویسا آزمایشی"),
    ("herava-demo", "هراوا آزمایشی"),
    ("larisa-demo", "لاریسا آزمایشی"),
    ("mehrava-demo", "مهراوا آزمایشی"),
    ("sorina-demo", "سورینا آزمایشی"),
    ("venora-demo", "ونورا آزمایشی"),
]
PROFILES = [
    ("skin-dry", "خشک", "skin"),
    ("skin-oily", "چرب", "skin"),
    ("skin-combination", "مختلط", "skin"),
    ("skin-sensitive", "حساس", "skin"),
    ("skin-normal", "معمولی", "skin"),
    ("skin-all", "همه انواع پوست", "skin"),
    ("hair-dry", "خشک", "hair"),
    ("hair-oily", "چرب", "hair"),
    ("hair-normal", "معمولی", "hair"),
    ("hair-curly", "فر و مجعد", "hair"),
    ("hair-colored", "رنگ‌شده", "hair"),
    ("hair-fine", "نازک", "hair"),
    ("hair-all", "همه انواع مو", "hair"),
]
CONCERNS = [
    ("hydration", "آبرسانی", "skin", "رطوبت دهیدراته کم آبی hydration moisturizer"),
    ("dryness", "خشکی پوست", "skin", "خشک کشیدگی پوسته dryness"),
    ("oil-control", "چربی پوست", "skin", "چرب برق صورت oily sebum"),
    ("skin-cleansing", "پاک‌سازی پوست", "skin", "شستشو شوینده تمیز cleanser"),
    ("sensitive-care", "مراقبت پوست حساس", "skin", "حساس بدون عطر ملایم sensitive"),
    ("sun-care", "محافظت آفتاب", "skin", "آفتاب ضدآفتاب sunscreen"),
    ("uneven-look", "ظاهر ناهمگون پوست", "skin", "کدر یکنواخت لک ظاهر uneven"),
    ("lip-dryness", "خشکی لب", "skin", "لب بالم ترک lip"),
    ("hand-care", "مراقبت دست", "skin", "دست hand"),
    ("body-care", "مراقبت بدن", "skin", "بدن body"),
    ("makeup-removal", "پاک کردن آرایش", "skin", "آرایش میسلار پاک کننده micellar"),
    ("hair-cleansing", "شست‌وشوی مو", "hair", "شامپو مو shampoo wash"),
    ("hair-dryness", "خشکی مو", "hair", "موی خشک زبر dry hair"),
    ("frizz", "وز مو", "hair", "وز گره مجعد frizz"),
    ("colored-care", "مراقبت موی رنگ‌شده", "hair", "رنگ شده رنگ‌شده دکلره colored"),
    ("scalp-oil", "چربی پوست سر", "hair", "سر چرب scalp oily"),
    ("curl-style", "حالت‌دهی موی فر", "hair", "فر مجعد curl"),
    ("volume", "حجم‌دهی ظاهری مو", "hair", "حجم نازک کم حجم volume"),
    ("heat-style", "مراقبت هنگام حالت‌دهی", "hair", "حرارت سشوار اتو heat"),
    ("detangle", "باز کردن گره مو", "hair", "گره شانه detangle"),
    ("hair-shine", "درخشندگی مو", "hair", "براق درخشان shine"),
    ("hold-style", "تثبیت حالت مو", "hair", "حالت ژل واکس اسپری hold"),
    ("coverage", "پوشش آرایش صورت", "beauty", "کرم پودر پوشانندگی foundation concealer"),
    ("matte-look", "آرایش مات", "beauty", "مات برق matte"),
    ("glow-look", "آرایش درخشان", "beauty", "براق شاین درخشان glow"),
    ("lip-color", "رنگ لب", "beauty", "رژ لب lipstick gloss"),
    ("eye-color", "آرایش چشم", "beauty", "چشم سایه خط چشم mascara"),
    ("brow-style", "آرایش ابرو", "beauty", "ابرو مداد ژل brow"),
    ("cheek-color", "رنگ گونه", "beauty", "گونه رژگونه blush"),
    ("makeup-fix", "تثبیت آرایش", "beauty", "فیکس تثبیت primer setting"),
    ("nail-color", "رنگ ناخن", "beauty", "ناخن لاک nail polish"),
]
INGREDIENTS = [
    ("glycerin", "گلیسیرین", "Glycerin"),
    ("ceramide", "سرامید", "Ceramide NP"),
    ("hyaluronate", "سدیم هیالورونات", "Sodium Hyaluronate"),
    ("panthenol", "پانتنول", "Panthenol"),
    ("niacinamide", "نیاسینامید", "Niacinamide"),
    ("squalane", "اسکوالان", "Squalane"),
    ("aloe", "آلوئه‌ورا", "Aloe Barbadensis Leaf Juice"),
    ("shea", "شی باتر", "Butyrospermum Parkii Butter"),
    ("zinc-oxide", "زینک اکساید", "Zinc Oxide"),
    ("coco-glucoside", "کوکو گلوکوزاید", "Coco-Glucoside"),
    ("argan", "روغن آرگان", "Argania Spinosa Kernel Oil"),
    ("jojoba", "روغن جوجوبا", "Simmondsia Chinensis Seed Oil"),
    ("dimethicone", "دایمتیکون", "Dimethicone"),
    ("keratin", "کراتین هیدرولیزشده", "Hydrolyzed Keratin"),
    ("polyquaternium", "پلی‌کواترنیوم", "Polyquaternium-10"),
    ("silica", "سیلیکا", "Silica"),
    ("mica", "میکا", "Mica"),
    ("iron-oxides", "اکسیدهای آهن", "Iron Oxides"),
    ("beeswax", "موم زنبور", "Cera Alba"),
    ("castor", "روغن کرچک", "Ricinus Communis Seed Oil"),
    ("pvp", "پلی‌وینیل پیرولیدون", "PVP"),
    ("fragrance", "عطر", "Parfum"),
]

# slug, category, product, profiles, concerns, ingredients, instructions, pack unit/base
SPECS = {
    "skin": [
        ("face-moisturizer",
            "مرطوب‌کننده صورت",
            "کرم مرطوب‌کننده",
            "skin-dry skin-normal",
            "hydration dryness",
            "glycerin ceramide",
            "روی پوست تمیز استفاده شود.",
            "ml",
            50),
        ("hydrating-serum",
            "سرم آبرسان",
            "سرم آبرسان",
            "skin-dry skin-combination",
            "hydration dryness",
            "hyaluronate panthenol",
            "چند قطره روی پوست تمیز استفاده شود.",
            "ml",
            30),
        ("gentle-cleanser",
            "شوینده صورت",
            "ژل شوینده ملایم",
            "skin-sensitive skin-normal",
            "skin-cleansing sensitive-care",
            "coco-glucoside glycerin",
            "روی پوست مرطوب استفاده و سپس آبکشی شود.",
            "ml",
            150),
        ("oil-cleanser",
            "شوینده پوست چرب",
            "فوم شوینده پوست چرب",
            "skin-oily skin-combination",
            "skin-cleansing oil-control",
            "coco-glucoside niacinamide",
            "روی پوست مرطوب استفاده و آبکشی شود.",
            "ml",
            150),
        ("light-gel",
            "ژل مرطوب‌کننده",
            "ژل مرطوب‌کننده سبک",
            "skin-oily skin-combination",
            "hydration oil-control",
            "glycerin aloe",
            "لایه نازکی روی پوست تمیز استفاده شود.",
            "ml",
            50),
        ("sensitive-cream",
            "مراقبت پوست حساس",
            "کرم ملایم پوست حساس",
            "skin-sensitive skin-dry",
            "sensitive-care dryness",
            "ceramide squalane",
            "مطابق دستور برچسب استفاده شود.",
            "ml",
            50),
        ("sun-screen",
            "ضدآفتاب",
            "لوسیون نمونه مراقبت آفتاب",
            "skin-all skin-sensitive",
            "sun-care",
            "zinc-oxide glycerin",
            "دستور و مقدار مصرف باید از برچسب واقعی محصول تأیید شود؛ نمونه فاقد SPF تأییدشده است.",
            "ml",
            50),
        ("micellar",
            "میسلار واتر",
            "محلول پاک‌کننده آرایش",
            "skin-all skin-sensitive",
            "makeup-removal skin-cleansing",
            "glycerin panthenol",
            "با پد روی پوست استفاده شود؛ از تماس با چشم خودداری شود.",
            "ml",
            200),
        ("cleansing-balm",
            "بالم پاک‌کننده",
            "بالم پاک‌کننده صورت",
            "skin-dry skin-normal",
            "makeup-removal",
            "squalane shea",
            "روی پوست ماساژ داده و آبکشی شود.",
            "g",
            80),
        ("body-lotion",
            "لوسیون بدن",
            "لوسیون رطوبت بدن",
            "skin-dry skin-all",
            "body-care dryness",
            "glycerin shea",
            "روی پوست بدن استفاده شود.",
            "ml",
            250),
        ("hand-cream",
            "کرم دست",
            "کرم مراقبت دست",
            "skin-dry skin-all",
            "hand-care dryness",
            "shea panthenol",
            "روی پوست دست استفاده شود.",
            "ml",
            75),
        ("lip-balm",
            "بالم لب",
            "بالم نرم‌کننده لب",
            "skin-all",
            "lip-dryness",
            "beeswax castor",
            "لایه نازک روی لب استفاده شود.",
            "g",
            5),
        ("face-mask",
            "ماسک صورت",
            "ماسک رطوبت صورت",
            "skin-dry skin-normal",
            "hydration",
            "aloe hyaluronate",
            "مدت استفاده مطابق برچسب محصول تعیین شود.",
            "ml",
            75),
        ("tone-serum",
            "سرم یکنواخت‌کننده ظاهر",
            "سرم مراقبت ظاهر پوست",
            "skin-normal skin-combination",
            "uneven-look",
            "niacinamide glycerin",
            "روی پوست تمیز و طبق برچسب استفاده شود.",
            "ml",
            30),
        ("body-wash",
            "شوینده بدن",
            "شوینده ملایم بدن",
            "skin-all skin-sensitive",
            "body-care skin-cleansing",
            "coco-glucoside panthenol",
            "روی پوست مرطوب بدن استفاده و آبکشی شود.",
            "ml",
            300),
    ],
    "hair": [
        ("daily-shampoo",
            "شامپو روزانه",
            "شامپو روزانه ملایم",
            "hair-normal hair-all",
            "hair-cleansing",
            "coco-glucoside panthenol",
            "روی موی خیس ماساژ داده و آبکشی شود.",
            "ml",
            250),
        ("dry-shampoo",
            "شامپو موی خشک",
            "شامپو مراقبت موی خشک",
            "hair-dry",
            "hair-cleansing hair-dryness",
            "glycerin argan",
            "روی موی خیس استفاده و آبکشی شود.",
            "ml",
            250),
        ("oily-shampoo",
            "شامپو موی چرب",
            "شامپو پوست سر چرب",
            "hair-oily",
            "hair-cleansing scalp-oil",
            "coco-glucoside niacinamide",
            "روی پوست سر مرطوب استفاده و کامل آبکشی شود.",
            "ml",
            250),
        ("color-shampoo",
            "شامپو موی رنگ‌شده",
            "شامپو موی رنگ‌شده",
            "hair-colored",
            "hair-cleansing colored-care",
            "panthenol keratin",
            "روی موی خیس استفاده و آبکشی شود.",
            "ml",
            250),
        ("curl-shampoo",
            "شامپو موی فر",
            "شامپو موی فر",
            "hair-curly hair-dry",
            "hair-cleansing curl-style",
            "coco-glucoside aloe",
            "روی موی خیس استفاده و آبکشی شود.",
            "ml",
            250),
        ("conditioner",
            "نرم‌کننده مو",
            "نرم‌کننده ساقه مو",
            "hair-dry hair-normal",
            "detangle hair-dryness",
            "polyquaternium panthenol",
            "روی ساقه مو استفاده و آبکشی شود.",
            "ml",
            200),
        ("hair-mask",
            "ماسک مو",
            "ماسک مراقبت ساقه مو",
            "hair-dry hair-colored",
            "hair-dryness colored-care",
            "keratin argan",
            "روی ساقه استفاده شود؛ مدت ماندن طبق برچسب.",
            "ml",
            250),
        ("leave-in",
            "کرم مو بدون آبکشی",
            "کرم مو بدون آبکشی",
            "hair-curly hair-dry",
            "frizz detangle",
            "panthenol shea",
            "مقدار کم روی ساقه مرطوب؛ بدون آبکشی.",
            "ml",
            150),
        ("hair-serum",
            "سرم مو",
            "سرم ساقه مو",
            "hair-dry hair-colored",
            "frizz hair-shine",
            "dimethicone argan",
            "مقدار کم روی ساقه استفاده شود.",
            "ml",
            50),
        ("hair-oil",
            "روغن مو",
            "روغن سبک ساقه مو",
            "hair-dry hair-curly",
            "hair-shine hair-dryness",
            "argan jojoba",
            "مقدار کم روی ساقه استفاده شود.",
            "ml",
            50),
        ("heat-spray",
            "اسپری حالت‌دهی حرارتی",
            "اسپری مراقبت حالت‌دهی",
            "hair-all",
            "heat-style",
            "dimethicone panthenol",
            "طبق برچسب قبل از حالت‌دهی؛ دمای ایمن از ابزار بررسی شود.",
            "ml",
            150),
        ("curl-cream",
            "کرم موی فر",
            "کرم حالت‌دهنده موی فر",
            "hair-curly",
            "curl-style frizz",
            "shea polyquaternium",
            "روی موی مرطوب استفاده شود.",
            "ml",
            150),
        ("volume-mousse",
            "موس مو",
            "موس حجم‌دهنده ظاهری",
            "hair-fine hair-normal",
            "volume hold-style",
            "pvp panthenol",
            "مقدار کم روی مو و طبق برچسب استفاده شود.",
            "ml",
            150),
        ("hair-gel",
            "ژل مو",
            "ژل تثبیت حالت مو",
            "hair-all",
            "hold-style",
            "pvp aloe",
            "روی مو برای حالت‌دهی استفاده شود.",
            "ml",
            150),
        ("detangle-spray",
            "اسپری گره‌بازکن",
            "اسپری گره‌بازکن مو",
            "hair-curly hair-fine",
            "detangle frizz",
            "polyquaternium panthenol",
            "روی ساقه مرطوب اسپری و به‌آرامی شانه شود.",
            "ml",
            150),
    ],
    "beauty": [
        ("foundation",
            "کرم پودر",
            "کرم پودر",
            "skin-normal skin-combination",
            "coverage",
            "iron-oxides dimethicone",
            "روی پوست آماده برای آرایش استفاده شود.",
            "ml",
            30),
        ("concealer",
            "کانسیلر",
            "کانسیلر",
            "skin-all",
            "coverage",
            "iron-oxides glycerin",
            "مقدار کم برای پوشش ظاهری استفاده شود.",
            "ml",
            10),
        ("bb-cream",
            "بی‌بی کرم",
            "بی‌بی کرم",
            "skin-normal skin-dry",
            "coverage hydration",
            "iron-oxides hyaluronate",
            "روی پوست تمیز استفاده شود.",
            "ml",
            30),
        ("face-powder",
            "پودر صورت",
            "پودر مات صورت",
            "skin-oily skin-combination",
            "matte-look makeup-fix",
            "silica mica",
            "با براش روی نواحی موردنظر استفاده شود.",
            "g",
            12),
        ("primer",
            "پرایمر",
            "پرایمر آرایش",
            "skin-all",
            "makeup-fix",
            "dimethicone glycerin",
            "پیش از آرایش روی پوست استفاده شود.",
            "ml",
            30),
        ("blush",
            "رژگونه",
            "رژگونه",
            "skin-all",
            "cheek-color",
            "mica iron-oxides",
            "با براش روی گونه استفاده شود.",
            "g",
            8),
        ("highlighter",
            "هایلایتر",
            "هایلایتر",
            "skin-all",
            "glow-look",
            "mica silica",
            "روی نواحی موردنظر صورت استفاده شود.",
            "g",
            8),
        ("bronzer",
            "برنزر",
            "برنزر",
            "skin-all",
            "coverage matte-look",
            "mica iron-oxides",
            "با براش برای رنگ ظاهری صورت استفاده شود.",
            "g",
            10),
        ("lipstick",
            "رژ لب",
            "رژ لب جامد",
            "skin-all",
            "lip-color",
            "castor beeswax",
            "روی لب استفاده شود.",
            "g",
            4),
        ("lip-gloss",
            "برق لب",
            "برق لب",
            "skin-all",
            "lip-color glow-look",
            "castor squalane",
            "لایه نازکی روی لب استفاده شود.",
            "ml",
            6),
        ("eye-shadow",
            "سایه چشم",
            "سایه چشم",
            "skin-all",
            "eye-color",
            "mica iron-oxides",
            "روی پلک؛ از تماس مستقیم با چشم خودداری شود.",
            "g",
            6),
        ("eyeliner",
            "خط چشم",
            "خط چشم",
            "skin-all",
            "eye-color",
            "iron-oxides pvp",
            "فقط طبق برچسب دور چشم استفاده شود.",
            "ml",
            3),
        ("mascara",
            "ریمل",
            "ریمل",
            "skin-all",
            "eye-color",
            "beeswax iron-oxides",
            "روی مژه طبق برچسب؛ با دیگران مشترک استفاده نشود.",
            "ml",
            9),
        ("brow-pencil",
            "مداد ابرو",
            "مداد ابرو",
            "skin-all",
            "brow-style",
            "beeswax iron-oxides",
            "برای شکل ظاهری ابرو استفاده شود.",
            "g",
            1),
        ("nail-polish",
            "لاک ناخن",
            "لاک ناخن",
            "skin-all",
            "nail-color",
            "iron-oxides mica",
            "روی ناخن در محیط دارای تهویه استفاده شود.",
            "ml",
            10),
    ],
}

def generate():
    profile_names = {x[0]: x[1] for x in PROFILES}
    concern_names = {x[0]: x[1] for x in CONCERNS}
    ingredient_names = {x[0]: f"{x[1]} ({x[2]})" for x in INGREDIENTS}
    search_terms = {x[0]: x[3] for x in CONCERNS}
    domains = {"skin": "پوست", "hair": "مو", "beauty": "زیبایی و آرایش"}
    categories = [{"slug": domain, "name": name, "domain": domain, "parent": None}
                  for domain, name in domains.items()]
    products = []
    for domain, specs in SPECS.items():
        for j, spec in enumerate(specs):
            slug, category, title, profile_list, concern_list, base_ingredients, usage, unit, size = spec
            categories.append({"slug": slug, "name": category, "domain": domain, "parent": domain})
            for b, (brand_slug, brand_name) in enumerate(BRANDS):
                number = len(products) + 1
                sku = f"DEMO-{domain.upper()}-{j+1:02}-{b+1:02}"
                profiles = profile_list.split()
                # Some variants target a narrower profile, others cover all listed profiles.
                if b % 3 == 1 and len(profiles) > 1:
                    profiles = [profiles[b % len(profiles)]]
                concerns = concern_list.split()
                ingredients = list(dict.fromkeys(base_ingredients.split() + ["panthenol" if b % 2 else "glycerin"]))
                fragrance_free = "skin-sensitive" in profiles or b % 2 == 0
                if not fragrance_free:
                    ingredients.append("fragrance")
                formula = "بدون عطر افزوده" if fragrance_free else "دارای عطر افزوده"
                price = 180000 + j * 65000 + b * 110000 + (250000 if domain == "beauty" else 0)
                inactive = number % 29 == 0
                unavailable = number % 13 == 0
                shades = [None, None, None]
                if domain == "beauty" and slug != "primer":
                    if slug in ("foundation", "concealer", "bb-cream", "face-powder", "bronzer"):
                        palette = ["روشن خنثی",
                            "روشن گرم",
                            "متوسط خنثی",
                            "متوسط گرم",
                            "تیره خنثی",
                            "تیره گرم"]
                    elif slug in ("mascara", "eyeliner", "brow-pencil"):
                        palette = ["مشکی", "قهوه‌ای روشن", "قهوه‌ای تیره", "خاکستری"]
                    else:
                        palette = ["صورتی", "هلویی", "رز", "قرمز", "نود", "بنفش", "طلایی", "مسی"]
                    shades = [palette[(b + v) % len(palette)] for v in range(3)]
                finish = ("مات" if b % 2 == 0 else "درخشان") if domain == "beauty" else None
                variants = []
                for v in range(3):
                    amount = size if domain == "beauty" else size * (1 + v)
                    stock = 0 if unavailable or (v == 0 and number % 5 == 0) else 5 + ((number * 7 + v * 11) % 71)
                    variants.append({"sku": f"{sku}-V{v+1}",
                        "name": f"{amount} {unit}" + (f" - {shades[v]}" if shades[v] else ""),
                                     "sizeValue": amount, "sizeUnit": unit, "shade": shades[v], "finish": finish,
                                     "price": price + v * 190000, "stockQuantity": stock, "isActive": not (number % 17 == 0 and v == 2)})
                products.append({
                    "sku": sku, "name": f"{title} {brand_name} - {formula}", "brandSlug": brand_slug,
                    "categorySlug": slug, "brand": brand_name, "category": category,
                    "skinTypes": "، ".join(profile_names[p] for p in profiles if p.startswith("skin-")),
                    "hairTypes": "، ".join(profile_names[p] for p in profiles if p.startswith("hair-")),
                    "concernsText": "، ".join(concern_names[c] for c in concerns),
                    "ingredientsText": "، ".join(ingredient_names[i] for i in ingredients),
                    "description": f"داده کاملاً ساختگی برای تست سامانه در دسته {domains[domain]} / {category}. فرمول نمونه {formula}. "
                                   + "این محصول، ترکیبات و قیمت‌ها واقعی نیستند و اثربخشی یا ایمنی بالینی تأیید نشده است.",
                    "warnings": "فقط داده آزمایشی؛ برای خرید یا توصیه پزشکی استفاده نشود. در محصول واقعی، برچسب و حساسیت به ترکیبات بررسی شود. "
                                + ("حاوی عطر افزوده در فرمول ساختگی." if not fragrance_free else "بدون عطر افزوده در فرمول ساختگی."),
                    "usageInstructions": usage, "searchKeywords": " ".join(search_terms[c] for c in concerns) + " " + formula,
                    "price": min(v["price"] for v in variants if v["isActive"]),
                    "stockQuantity": sum(v["stockQuantity"] for v in variants if v["isActive"]),
                    "isActive": not inactive, "profiles": profiles, "concerns": concerns,
                    "fragranceFree": fragrance_free,
                    "ingredients": ingredients, "variants": variants,
                })
    return {"isSynthetic": True, "currency": "IRR", "categories": categories,
            "brands": [{"slug": s, "name": n, "country": "ساختگی / آزمایشی"} for s, n in BRANDS],
            "profiles": [{"slug": s, "name": n, "kind": k} for s, n, k in PROFILES],
            "concerns": [{"slug": s, "name": n, "domain": d, "searchTerms": t} for s,
                n,
                d,
                t in CONCERNS],
            "ingredients": [{"slug": s, "name": n, "inciName": i} for s, n, i in INGREDIENTS], "products": products}

def seed_sql(data):
    payload = json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("'", "''")
    sql = """-- Generated by generate_demo.py. INSERT missing demo records only; preserve existing edits.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
BEGIN TRANSACTION;
DECLARE @data NVARCHAR(MAX) = N'""" + payload + "';\n"
    for table, array, extra in [
        ("Categories", "categories", "Domain nvarchar(20) '$.domain'"),
        ("Brands", "brands", "Country nvarchar(100) '$.country'"),
        ("Profiles", "profiles", "Kind nvarchar(20) '$.kind'"),
        ("Concerns",
            "concerns",
            "Domain nvarchar(20) '$.domain', SearchTerms nvarchar(500) '$.searchTerms'"),
        ("Ingredients", "ingredients", "InciName nvarchar(200) '$.inciName'"),
    ]:
        extra_cols = [part.strip().split()[0] for part in extra.split(",")]
        cols = "Slug,Name," + ",".join(extra_cols)
        sql += f"INSERT dbo.{table} ({cols}" + (",IsDemo" if table == "Brands" else "") + ")\n"
        sql += "SELECT " + ",".join("j." + c for c in ["Slug",
            "Name"] + extra_cols) + (",1" if table == "Brands" else "")
        sql += f" FROM OPENJSON(@data,'$.{array}') WITH (Slug nvarchar(80) '$.slug',Name nvarchar(100) '$.name',{extra}) j\n"
        sql += f"WHERE NOT EXISTS (SELECT 1 FROM dbo.{table} t WHERE t.Slug=j.Slug);\n"
    sql += """
UPDATE c SET ParentId=p.Id FROM dbo.Categories c
JOIN OPENJSON(@data,'$.categories') WITH (Slug nvarchar(80) '$.slug',Parent nvarchar(80) '$.parent') j ON j.Slug=c.Slug
JOIN dbo.Categories p ON p.Slug=j.Parent WHERE c.ParentId IS NULL;

SELECT j.*,b.Id AS BrandId,c.Id AS CategoryId INTO #SeedProducts
FROM OPENJSON(@data,'$.products') WITH (
    Sku nvarchar(80) '$.sku',Name nvarchar(200) '$.name',BrandSlug nvarchar(80) '$.brandSlug',CategorySlug nvarchar(80) '$.categorySlug',
    Brand nvarchar(100) '$.brand',Category nvarchar(100) '$.category',SkinTypes nvarchar(300) '$.skinTypes',HairTypes nvarchar(300) '$.hairTypes',
    Concerns nvarchar(500) '$.concernsText',Ingredients nvarchar(max) '$.ingredientsText',Description nvarchar(max) '$.description',
    Warnings nvarchar(max) '$.warnings',UsageInstructions nvarchar(2000) '$.usageInstructions',SearchKeywords nvarchar(1000) '$.searchKeywords',
    Price decimal(18,2) '$.price',StockQuantity int '$.stockQuantity',IsActive bit '$.isActive',FragranceFree bit '$.fragranceFree',
    ProfileJson nvarchar(max) '$.profiles' AS JSON,ConcernJson nvarchar(max) '$.concerns' AS JSON,
    IngredientJson nvarchar(max) '$.ingredients' AS JSON,VariantJson nvarchar(max) '$.variants' AS JSON
) j JOIN dbo.Brands b ON b.Slug=j.BrandSlug JOIN dbo.Categories c ON c.Slug=j.CategorySlug;

INSERT dbo.Products (Sku,Name,Brand,Category,BrandId,CategoryId,SkinTypes,HairTypes,Concerns,Ingredients,Description,Warnings,
                     UsageInstructions,SearchKeywords,Price,StockQuantity,IsActive,FragranceFree,IsDemo,Currency)
SELECT s.Sku,s.Name,s.Brand,s.Category,s.BrandId,s.CategoryId,s.SkinTypes,s.HairTypes,s.Concerns,s.Ingredients,s.Description,s.Warnings,
       s.UsageInstructions,s.SearchKeywords,s.Price,s.StockQuantity,s.IsActive,s.FragranceFree,1,'IRR'
FROM #SeedProducts s WHERE NOT EXISTS (SELECT 1 FROM dbo.Products p WHERE p.Sku=s.Sku);
UPDATE p SET FragranceFree=s.FragranceFree FROM dbo.Products p JOIN #SeedProducts s ON p.Sku=s.Sku
WHERE p.IsDemo=1 AND p.FragranceFree IS NULL;
"""
    for link, lookup, key, json_col in [
        ("ProductProfiles", "Profiles", "ProfileId", "ProfileJson"),
        ("ProductConcerns", "Concerns", "ConcernId", "ConcernJson"),
        ("ProductIngredients", "Ingredients", "IngredientId", "IngredientJson"),
    ]:
        sql += f"""
INSERT dbo.{link} (ProductId,{key})
SELECT p.Id,t.Id FROM #SeedProducts s JOIN dbo.Products p ON p.Sku=s.Sku
CROSS APPLY OPENJSON(s.{json_col}) j JOIN dbo.{lookup} t ON t.Slug=j.value
WHERE NOT EXISTS (SELECT 1 FROM dbo.{link} x WHERE x.ProductId=p.Id AND x.{key}=t.Id);
"""
    sql += """
INSERT dbo.ProductVariants (ProductId,Sku,Name,SizeValue,SizeUnit,Shade,Finish,Price,StockQuantity,IsActive)
SELECT p.Id,v.Sku,v.Name,v.SizeValue,v.SizeUnit,v.Shade,v.Finish,v.Price,v.StockQuantity,v.IsActive
FROM #SeedProducts s JOIN dbo.Products p ON p.Sku=s.Sku
CROSS APPLY OPENJSON(s.VariantJson) WITH (
    Sku nvarchar(100) '$.sku',Name nvarchar(200) '$.name',SizeValue decimal(10,2) '$.sizeValue',SizeUnit nvarchar(10) '$.sizeUnit',
    Shade nvarchar(100) '$.shade',Finish nvarchar(100) '$.finish',Price decimal(18,2) '$.price',StockQuantity int '$.stockQuantity',IsActive bit '$.isActive'
) v WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductVariants x WHERE x.Sku=v.Sku);

-- Associate the three legacy sample products without changing their descriptive fields.
UPDATE p SET CategoryId=c.Id FROM dbo.Products p JOIN dbo.Categories c ON c.Slug='face-moisturizer'
WHERE p.CategoryId IS NULL AND p.Category=N'مرطوب‌کننده';
UPDATE p SET CategoryId=c.Id FROM dbo.Products p JOIN dbo.Categories c ON c.Slug='gentle-cleanser'
WHERE p.CategoryId IS NULL AND p.Category=N'شوینده';
IF NOT EXISTS (SELECT 1 FROM dbo.Brands WHERE Slug='legacy-sample')
INSERT dbo.Brands (Slug,Name,IsDemo) VALUES ('legacy-sample',N'برند نمونه',1);
UPDATE p SET BrandId=b.Id FROM dbo.Products p JOIN dbo.Brands b ON b.Slug='legacy-sample'
WHERE p.BrandId IS NULL AND p.Brand=N'برند نمونه';
-- Label only the original known sample rows, never unrelated products.
UPDATE dbo.Products SET IsDemo=1 WHERE Id IN (1,2,3) AND Brand=N'برند نمونه'
AND Name IN (N'مرطوب‌کننده سبک صورت',N'شوینده ملایم صورت',N'کرم مرطوب‌کننده پوست خشک');
COMMIT TRANSACTION;
SELECT COUNT(*) AS TotalProducts,SUM(CAST(IsDemo AS int)) AS DemoProducts FROM dbo.Products;
SELECT COUNT(*) AS TotalVariants FROM dbo.ProductVariants;
"""
    return sql

if __name__ == "__main__":
    data = generate()
    (ROOT / "seed-demo.sql").write_text(seed_sql(data), encoding="utf-8-sig")
    print(f"Generated {len(data['products'])} synthetic products, {sum(len(p['variants']) for p in data['products'])} variants.")
