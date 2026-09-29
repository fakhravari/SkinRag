# ساختار و فرمت کد

## مسئولیت بخش‌ها

| مسیر | مسئولیت |
| --- | --- |
| `Program.cs` | شروع برنامه |
| `Hosting` | ثبت وابستگی‌ها، میان‌افزارها و مسیرهای سلامت |
| `Controllers` | دریافت درخواست HTTP و ارسال پاسخ |
| `Application/Abstractions` | قرارداد دسترسی به مدل و مخزن محصولات |
| `Application/Consultation` | هماهنگی مراحل، حافظه گفتگو و ساخت متن معتبر |
| `Application/Intent` | تشخیص نیت و قرارداد خروجی آن |
| `Application/Retrieval` | ساخت فیلترها و بازیابی محصول |
| `Application/Validation` | بررسی ورودی و پیشنهادها |
| `Infrastructure/Persistence` | پیاده‌سازی مخزن SQL با EF Core |
| `Services` | کلاینت Ollama، ایندکس دانش و خدمات کاتالوگ |
| `Prompts` | پرامپت‌ها و ساختار JSON مدل |
| `Models` و `Data` | مدل‌های داده، قرارداد API و نگاشت دیتابیس |
| `Views` و `wwwroot/assets` | صفحات Razor، JavaScript و CSS |
| `tests/SkinRag.Checks` | بررسی مستقل قراردادها و رفتار خط پردازش |

کنترلرها عملیات مشاوره را به سرویس کاربردی می‌سپارند. پیاده‌سازی SQL و Ollama از طریق قراردادهای `Application/Abstractions` تزریق می‌شود؛ محل ثبت آن‌ها `Hosting/ServiceCollectionExtensions.cs` است.

`ConsultationService` مراحل را هماهنگ می‌کند. `ConsultationSchema` ساختار خروجی مدل را تعیین می‌کند و `ConsultationAnswerFormatter` متن قیمت و مشخصات را از داده‌های معتبر می‌سازد. در `QueryBuilder` استخراج مدل و ادغام فیلترها متدهای مستقل دارند.

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
