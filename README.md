<div dir="rtl" style="direction: rtl; text-align: right;">

# SkinRag؛ کاتالوگ و مشاور محصولات پوست، مو و زیبایی

SkinRag یک برنامهٔ وب با **ASP.NET Core و .NET 10** است. کاربران می‌توانند محصولات را ببینند یا در گفت‌وگو، بر پایهٔ اطلاعات کاتالوگ محصول بگیرند. برنامه اطلاعات را از **SQL Server** می‌خواند و برای فهم درخواست و جست‌وجوی معنایی از مدل‌های **Ollama** استفاده می‌کند. رابط کاربری و پنل مدیریت با Razor، JavaScript و CSS ساخته شده‌اند.

## از کجا شروع کنم؟

برای اجرای محلی، به .NET 10 SDK، یک نمونهٔ SQL Server با پایگاه دادهٔ سازگار با مدل برنامه، و Ollama نیاز دارید. API خودش طرح پایگاه داده یا دادهٔ نمونه نمی‌سازد؛ فایل پشتیبان موجود در مخزن در `database/SkinRagDb.bak` است و راهنمای فایل‌های پایگاه داده در `database/help.txt` قرار دارد. پیش از بازیابی نسخهٔ پشتیبان، راهنمای آن را بخوانید.

۱. مقدار `ConnectionStrings:DefaultConnection` را در `SkinRag.Api/appsettings.json` یا متغیر محیطی `ConnectionStrings__DefaultConnection` برای SQL Server خود تنظیم کنید. از ثبت گذرواژه یا اطلاعات محرمانه در مخزن خودداری کنید.

۲. Ollama را اجرا کنید و مدل‌های تنظیم‌شدهٔ برنامه را دریافت کنید:

<div dir="ltr">

```powershell
ollama pull nomic-embed-text
ollama pull qwen2.5:7b
ollama pull qwen2.5:3b
```

</div>

۳. برنامه را از پوشهٔ مخزن اجرا کنید:

<div dir="ltr">

```powershell
dotnet run --project SkinRag.Api
```

</div>

۴. نشانی محلی اجرا در خروجی فرمان یا `SkinRag.Api/Properties/launchSettings.json` دیده می‌شود. صفحهٔ اصلی به `/chat` می‌رود؛ پنل مدیریت در `/admin` و مستندات Swagger در محیط Development در `/swagger` در دسترس‌اند.

هنگام بالا آمدن برنامه، سرویس پس‌زمینه فهرست محصولات فعال را می‌خواند و برای محصولاتِ نیازمند آن، embedding می‌سازد. آماده شدن این فهرست به اتصال SQL Server و در صورت نیاز به Ollama وابسته است. وضعیت آن را از `GET /api/knowledge/status` یا `/health/ready` بررسی کنید.

## برنامه چگونه کار می‌کند؟

پروژه یک برنامهٔ واحد ASP.NET Core است؛ پوشه‌های آن بخش‌های مفهومی کد هستند، نه پروژه‌ها یا سرویس‌های مستقل. نقطهٔ شروع برنامه `SkinRag.Api/Program.cs` است: سرویس‌ها و تنظیمات در `Hosting/ServiceCollectionExtensions.cs` ثبت می‌شوند و مسیرها و middlewareها در `Hosting/WebApplicationExtensions.cs` پیکربندی می‌شوند.

درخواست مشاوره از صفحهٔ چت به `POST /api/consultation/ask` می‌رود. کنترلر ورودی را به `ConsultationService` می‌سپارد؛ این سرویس نیت درخواست را تشخیص می‌دهد، زمینهٔ گفت‌وگو و محدودیت‌هایی مانند بودجه را در نظر می‌گیرد، و از `QueryBuilder` یک برنامهٔ جست‌وجو می‌سازد. سپس `ProductRetriever` محصولات واجد شرایط را از SQL می‌گیرد و با بردارهای دانش رتبه‌بندی می‌کند. در پایان، `RecommendationValidator` پیشنهادها را با محصولات مجاز تطبیق می‌دهد و پاسخ ساخت‌یافته به رابط کاربری برمی‌گردد.

جست‌وجوی معنایی روی snapshot حافظه‌ای از دانش محصولات انجام می‌شود. `KnowledgeIndexWorker` در شروع برنامه و سپس در بازهٔ `Rag:RefreshIntervalSeconds` از کاتالوگ snapshot را به‌روز می‌کند. متن هر محصول با `Ollama:EmbeddingModel` به بردار تبدیل می‌شود و بردارها و هش متن در جدول `ProductEmbeddings` ذخیره می‌شوند تا در بازسازی‌های بعدی قابل استفاده باشند. با `POST /api/knowledge/rebuild?force=true` می‌توان همهٔ بردارها را دوباره ساخت؛ این کار به Ollama نیاز دارد.

اطلاعات جاری قیمت و موجودی از پایگاه داده خوانده می‌شوند؛ در پاسخ مشاوره به snapshot embedding وابسته نیستند. مدل زبانی درخواست را پردازش و متن پاسخ را تولید می‌کند، اما اعتبار پیشنهاد و جزئیات محصول از داده‌های کاتالوگ می‌آیند.

## راهنمای پوشه‌ها

| مسیر | کاربرد |
| --- | --- |
| `SkinRag.Api/Application/` | منطق مشاوره، تشخیص نیت، ساخت جست‌وجو، بازیابی و اعتبارسنجی |
| `SkinRag.Api/Controllers/` | مسیرهای HTTP برای صفحه‌ها، کاتالوگ، مشاوره و وضعیت دانش |
| `SkinRag.Api/Hosting/` | ثبت وابستگی‌ها، تنظیم middleware و مسیرهای سلامت |
| `SkinRag.Api/Infrastructure/` | ارتباط با SQL Server و Ollama و مدیریت خطا |
| `SkinRag.Api/Services/` | عملیات کاتالوگ و ساخت/نگه‌داری نمایهٔ دانش |
| `SkinRag.Api/Data/` و `Models/` | `DbContext`، موجودیت‌ها و قراردادهای داده |
| `SkinRag.Api/Views/` و `wwwroot/` | صفحه‌های Razor، کدهای رابط کاربری، قلم و منابع ایستا |
| `SkinRag.Api/Prompts/` | دستورها و ساختار خروجی مورد انتظار از مدل‌ها |
| `database/` | فایل‌های مرتبط با پایگاه داده؛ جزئیات را در `help.txt` ببینید |
| `docs/` | توضیح معماری و خط پردازش مشاوره |
| `tests/SkinRag.Checks/` | بررسی‌های مستقل منطق و قراردادها |

## پایگاه داده و دادهٔ محصولات

برنامه با SQL Server و EF Core کار می‌کند. `AppDbContext` نگاشت جدول‌ها را تعریف می‌کند؛ `ProductRepository` جست‌وجوی موردنیاز مشاوره را انجام می‌دهد و `CatalogService` داده‌های کاتالوگ را برای API می‌خواند. جدول‌های اصلی شامل محصولات، دسته‌ها، برندها، پروفایل‌ها، دغدغه‌ها، ترکیبات، گونه‌های محصول و بردارهای محصولات هستند.

محصولات دارای گونه می‌توانند قیمت و موجودی را برای هر اندازه یا رنگ نگه دارند؛ در محصولات قدیمیِ بدون گونه از فیلدهای خود محصول استفاده می‌شود. دادهٔ نمونه یا آزمایشی را با دادهٔ تأییدشدهٔ فروشنده اشتباه نگیرید. ساختار دقیق نصب و دادهٔ موجود در مخزن را از `database/help.txt` بررسی کنید؛ در مخزن اسکریپت نصب یا seed خودکار معرفی نشده است.

## مسیرهای مهم

| روش و مسیر | کاربرد |
| --- | --- |
| `GET /api/catalog/filters` | دریافت گزینه‌های معتبر فیلتر |
| `GET /api/catalog/stats` | دریافت آمار کاتالوگ |
| `GET /api/catalog/products` | جست‌وجو و صفحه‌بندی محصولات |
| `GET /api/catalog/products/{id}` | دریافت جزئیات یک محصول |
| `POST /api/consultation/ask` | ارسال درخواست مشاوره |
| `GET /api/knowledge/status` | مشاهدهٔ وضعیت نمایهٔ دانش |
| `POST /api/knowledge/rebuild?force=false` | بررسی و بازسازی نمایه با استفاده از cache معتبر |
| `GET /health/live` | بررسی زنده بودن برنامه |
| `GET /health/ready` | بررسی اتصال پایگاه داده و آماده بودن نمایه |

## تنظیمات اصلی

مقادیر پیش‌فرض در `SkinRag.Api/appsettings.json` قرار دارند و می‌توان آن‌ها را با تنظیمات محیطی جایگزین کرد.

| کلید | کاربرد |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | اتصال SQL Server |
| `Ollama:BaseUrl` | نشانی سرویس Ollama |
| `Ollama:ChatModel`، `IntentModel`، `QueryModel`، `ConsultationModel` | مدل‌های مراحل مختلف گفت‌وگو |
| `Ollama:EmbeddingModel` | مدل ساخت embedding محصولات و پرس‌وجو |
| `Rag:TopK` و `Rag:MinimumSimilarity` | تعداد و حداقل شباهت نتایج بازیابی |
| `Rag:EmbeddingBatchSize` | اندازهٔ هر دسته در ساخت بردارها |
| `Rag:DocumentPrefix` و `Rag:QueryPrefix` | پیشوند متن محصول و پرس‌وجو برای embedding |
| `Rag:EmbeddingVersion` | نسخهٔ متن و تنظیمات نمایه برای تشخیص اعتبار cache |
| `Rag:RefreshIntervalSeconds` | فاصلهٔ به‌روزرسانی پس‌زمینه؛ مقدار پیش‌فرض ۳۰۰ ثانیه است |

مدل embedding و مدل گفت‌وگو کارهای جداگانه انجام می‌دهند: `nomic-embed-text` برای بردارسازی است و مدل‌های `qwen2.5` برای مراحل گفت‌وگو به کار می‌روند. اگر مدل embedding یا روش ساخت متن دانش را تغییر دادید، نسخهٔ embedding را نیز متناسب با آن عوض کنید و نمایه را بازسازی کنید.

## ساخت و بررسی

برای ساخت راهکار:

<div dir="ltr">

```powershell
dotnet build SkinRag.sln
```

</div>

برای اجرای بررسی‌های مستقل منطق:

<div dir="ltr">

```powershell
dotnet run --project tests/SkinRag.Checks
```

</div>

این بررسی‌ها به اتصال زندهٔ SQL Server و Ollama نیاز ندارند. اجرای API برای کارکرد کامل مشاوره به تنظیمات و سرویس‌های گفته‌شده در بخش شروع نیاز دارد.

## مستندات تکمیلی

- [معماری و قواعد نگه‌داری کد](docs/code-architecture.md)
- [خط پردازش مشاوره و قراردادهای مدل](docs/consultation-pipeline.md)
- [راهنمای فایل‌های پایگاه داده](database/help.txt)

</div>
