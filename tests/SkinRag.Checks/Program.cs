using System.Diagnostics;
using SkinRag.Api.Infrastructure;
using SkinRag.Api.Models;
using SkinRag.Api.Services;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

string[] conversations =
[
    "سلام", "سلام رفیق، خوبی؟", "درود دوست عزیز", "سلاممم 😄", "سَلَام!",
    "سلام علیکم، وقت بخیر", "خوبی؟ چه خبر؟", "حالت چطوره؟", "خسته نباشید",
    "مرسییی", "دمت گرم رفیق", "خیلی ممنون، خداحافظ", "شب بخیر", "تو كي هستي؟",
    "سلام، خودت رو معرفی کن", "HI!", "خدا‌حافظ"
];
foreach (var message in conversations)
    Check(ConversationReplies.GetReply(message) is not null, $"Conversation sent to retrieval: {message}");

string[] productQuestions =
[
    "سلام یک شامپو می‌خواهم", "سلام برای پوست خشک چی داری؟", "مرسی، ارزان‌ترش چی؟",
    "خوبی؟ ضدآفتاب موجوده؟", "دمت گرم ترکیباتش چیه؟", "بله", "نه", "قیمتش؟", "۱۲۰۰۰۰۰",
    "سلام کرم", "خداحافظ نام یک محصول", "رفیق", "", "🙂", "anything unknown"
];
foreach (var message in productQuestions)
    Check(ConversationReplies.GetReply(message) is null, $"Product/context question was intercepted: {message}");

Check(PersianText.Normalize("  كرمِ   پوستِ خشك، يک! ") == "کرم پوست خشک یک", "Persian text normalization failed");
var document = new KnowledgeDocument(1, "کرم برای پوستِ خشک", [3, 4]);
Check(document.VectorNorm == 5, "Cached vector norm is incorrect");
Check(document.Tokens.SetEquals(["کرم", "پوست", "خشک"]), "Cached search tokens are incorrect");
Check(new KnowledgeDocument(2, "", [0, 0]).VectorNorm == 0, "Zero-vector metadata is incorrect");

// Exercise the real service with unavailable dependencies: small talk must return before SQL/index/model.
var service = new RagService(null!, null!, null!, null!, null!);
var stopwatch = Stopwatch.StartNew();
var reply = await service.AskAsync(new ConsultationRequest { Question = "سلام رفیق خوبی؟" }, default);
Check(reply.ResponseMode == "conversation" && reply.Products.Count == 0, "Conversation bypass failed");
Check(stopwatch.Elapsed < TimeSpan.FromSeconds(1), "Conversation response unexpectedly slow");
Console.WriteLine($"{checks} checks passed. Conversation service: {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
