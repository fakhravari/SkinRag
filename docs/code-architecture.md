# ساختار و فرمت کد

## مسئولیت بخش‌ها

| مسیر | مسئولیت |
| --- | --- |
| `Program.cs` | شروع برنامه |
| `Hosting` | ثبت وابستگی‌ها، میان‌افزارها و مسیرهای سلامت |
| `Controllers` | دریافت درخواست HTTP و ارسال پاسخ |
| `Application/Abstractions` | قراردادهای مدل و مخزن محصولات و قراردادهای درخواست/خطای مدل |
| `Application/Consultation` | هماهنگی مراحل، حافظه گفتگو و ساخت متن معتبر |
| `Application/Intent` | تشخیص نیت، موضوع گفتگو و پاسخ‌های محاوره‌ای |
| `Application/Retrieval` | ساخت فیلترها، بازیابی محصول و خطاهای ایندکس آماده‌نبودن |
| `Application/Validation` | بررسی ورودی و پیشنهادها |
| `Infrastructure/Persistence` | پیاده‌سازی مخزن SQL با EF Core |
| `Infrastructure/Ollama` | پیاده‌سازی قرارداد مدل از طریق Ollama |
| `Services/Catalog` | جست‌وجو، فیلتر و تبدیل داده‌های کاتالوگ |
| `Services/Knowledge` | ساخت، نگهداری و بازسازی ایندکس دانش |
| `Prompts` | پرامپت‌ها و ساختار JSON مدل |
| `Models` و `Data` | مدل‌های داده، قرارداد API و نگاشت دیتابیس |
| `Views` و `wwwroot/assets` | صفحات Razor، JavaScript و CSS |
| `tests/SkinRag.Checks` | بررسی مستقل قراردادها و رفتار خط پردازش |

کنترلرها عملیات را به لایهٔ کاربردی یا سرویس دامنه می‌سپارند. قرارداد Ollama و مخزن محصول در `Application/Abstractions` هستند؛ پیاده‌سازی‌های SQL و Ollama در `Infrastructure/Persistence` و `Infrastructure/Ollama` قرار دارند. ثبت وابستگی‌ها و ترکیب میزبان در `Hosting/ServiceCollectionExtensions.cs` انجام می‌شود.

`ConsultationService` مراحل را هماهنگ می‌کند. `ConsultationSchema` ساختار خروجی مدل را تعیین می‌کند و `ConsultationAnswerFormatter` متن قیمت و مشخصات را از داده‌های معتبر می‌سازد. در `QueryBuilder` استخراج مدل و ادغام فیلترها متدهای مستقل دارند. سرویس‌های هر دامنه در زیرپوشهٔ خودش نگه‌داری می‌شوند تا کلاینت مدل، بازسازی دانش و کاتالوگ در یک پوشهٔ عمومی مخلوط نشوند.

## فرمت در Visual Studio

تنظیمات `.editorconfig` با Ctrl+K، سپس Ctrl+D اعمال می‌شود. شرط‌ها بلوک مشخص دارند، خطوط طولانی چندخطی هستند و فاصله‌گذاری با چهار فاصله انجام می‌شود.

فرمت C# کل پروژه و بررسی‌ها:

```powershell
dotnet format whitespace SkinRag.sln --no-restore
dotnet format whitespace tests/SkinRag.Checks/SkinRag.Checks.csproj --no-restore
```

تنظیمات JavaScript، CSS و JSON در `.prettierrc.json` است. کتابخانه آماده jQuery از فرمت خارج است. فرمت کردن فایل‌های SQL به معنی اجرای آن‌ها نیست.

## اعتبارسنجی

```powershell
dotnet build SkinRag.sln --no-restore
dotnet run --project tests/SkinRag.Checks --no-restore
```
