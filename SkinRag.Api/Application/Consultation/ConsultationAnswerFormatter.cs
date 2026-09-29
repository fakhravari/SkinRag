using System.Globalization;
using SkinRag.Api.Application.Intent;
using SkinRag.Api.Application.Retrieval;
using SkinRag.Api.Models;

namespace SkinRag.Api.Application.Consultation;

internal static class ConsultationAnswerFormatter
{
    public static string Price(ProductDto p) => p.Price.HasValue ? $"قیمت ثبت‌شده از {p.Price.Value.ToString("N0", CultureInfo.InvariantCulture)} ریال." : "قیمت ثبت نشده است.";
    public static string InformationAnswer(IEnumerable<ProductMatch> matches, ConsultationIntent intent, CatalogVocabulary vocabulary) => string.Join(
                "\n\n",
                matches.Select(m =>
    {
        var p = m.Product;
        var text = $"[{p.Id}] {p.Name}\n{Price(p)}\n" + (p.StockQuantity > 0 ? $"موجودی ثبت‌شده: {p.StockQuantity} عدد." : "در حال حاضر ناموجود است.");
        if (intent is ConsultationIntent.ProductDetails or ConsultationIntent.ProductComparison)
        {
            var ingredients = p.Ingredients.Select(s => vocabulary.Ingredients.FirstOrDefault(x => x.Slug == s)?.Name ?? s);
            var formula = p.Ingredients.Length > 0 ? string.Join("، ", ingredients) : p.IngredientsText ?? "فهرست ترکیبات ثبت نشده است.";
            text += $"\nدسته: {p.Category}\nترکیبات ثبت‌شده: {formula}";
            if (!string.IsNullOrWhiteSpace(p.SkinTypes))
            {
                text += $"\nنوع پوست ثبت‌شده: {p.SkinTypes}";
            }

            if (!string.IsNullOrWhiteSpace(p.HairTypes))
            {
                text += $"\nنوع موی ثبت‌شده: {p.HairTypes}";
            }

            if (!string.IsNullOrWhiteSpace(p.UsageInstructions))
            {
                text += $"\nروش مصرف: {p.UsageInstructions}";
            }

            if (!string.IsNullOrWhiteSpace(p.Warnings))
            {
                text += $"\n{p.Warnings}";
            }
        }

        if (p.IsDemo)
        {
            text += "\nقیمت و مشخصات این رکورد هنوز با اطلاعات فروشنده تأیید نشده‌اند.";
        }

        return text;
    }));
}
