using System.Globalization;
using System.Text.RegularExpressions;
using SkinRag.Api.Application.Validation;

namespace SkinRag.Api.Application.Parsing;

public enum BudgetStatus
{
    None,
    Valid,
    MissingCurrency,
    Ambiguous
}

public sealed record ParsedBudget(BudgetStatus Status, decimal? MaximumPriceRials = null, bool IsBudgetOnly = false)
{
    public bool NeedsClarification => Status is BudgetStatus.MissingCurrency or BudgetStatus.Ambiguous;

    public string? FollowUpQuestion => Status switch
    {
        BudgetStatus.MissingCurrency => "مبلغ بودجه را با واحد ریال یا تومان می‌فرمایید؟",
        BudgetStatus.Ambiguous => "چند مبلغ متفاوت نوشته‌اید؛ سقف بودجه موردنظرتان کدام است؟ لطفاً مبلغ و واحد را مشخص کنید.",
        _ => null
    };
}

// Amounts entering catalog filters always use the database unit: IRR.
public static partial class BudgetParser
{
    public const decimal MaximumAllowedRials = 1_000_000_000m;

    private const string Amount = @"(?<amount>[+-]?[0-9]+(?:[.,٫٬][0-9]+)*)\s*(?<scale>هزار|میلیون|میلیارد)?\s*(?<currency>تومان|تومن|ریال)?(?![\p{L}\p{N}])";

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:تا|زیر|حداکثر|بودجه(?:\s+(?:من|ام))?(?:\s+(?:تا|است|هست|حدود))?|سقف(?:\s+قیمت)?|با(?:\s+بودجه)?)\s*(?:قیمت\s*)?" + Amount)]
    private static partial Regex BudgetLimit();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])من\s+(?:(?:فقط|حدود|تقریبا)\s+)*" + Amount + @"\s*(?:پول\s+)?دارم\b")]
    private static partial Regex AvailableMoney();

    [GeneratedRegex(@"^" + Amount + @"(?:\s+(?:دارم|پول دارم|بودجه دارم))?\s*[.!؟?]*$")]
    private static partial Regex StandaloneAmount();

    [GeneratedRegex(@"^(?:(?:سلام|درود|رفیق|من|فقط|حدود|تقریبا|ممنون)[\s،,!؟?]*)*$")]
    private static partial Regex SocialPrefix();

    [GeneratedRegex(@"^(?:(?:دارم|داریم|است|هست|هستش|پول دارم|بودجه دارم)\s*)*[.!؟?،,]*$")]
    private static partial Regex BudgetSuffix();

    [GeneratedRegex(@"^[0-9]{1,3}(?:[.,٬][0-9]{3})+$")]
    private static partial Regex GroupedNumber();

    [GeneratedRegex(@"^(?:ml|g|pcs|میلی لیتر|میلیلیتر|گرم|عدد|روز|سال|درصد)(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NonMoneyUnit();

    public static ParsedBudget Parse(string? input)
    {
        var text = InputNormalizer.Normalize(input);
        var matches = BudgetLimit().Matches(text).Cast<Match>()
            .Concat(AvailableMoney().Matches(text).Cast<Match>())
            .Where(m => m.Groups["currency"].Length > 0
                || !NonMoneyUnit().IsMatch(text[(m.Index + m.Length)..].TrimStart())).ToArray();
        if (matches.Length == 0)
        {
            var standalone = StandaloneAmount().Match(text);
            if (!standalone.Success || (standalone.Groups["currency"].Length == 0
                && standalone.Groups["scale"].Length == 0 && !text.Contains("دارم", StringComparison.Ordinal)))
            {
                return new(BudgetStatus.None);
            }

            matches = [standalone];
        }

        var isBudgetOnly = matches.Length == 1
            && SocialPrefix().IsMatch(text[..matches[0].Index].Trim())
            && BudgetSuffix().IsMatch(text[(matches[0].Index + matches[0].Length)..].Trim());
        if (matches.Any(m => m.Groups["currency"].Length == 0))
        {
            return new(BudgetStatus.MissingCurrency, IsBudgetOnly: isBudgetOnly);
        }

        var amounts = matches.Select(ConvertToRials).Distinct().ToArray();
        return amounts.Length == 1
            ? new(BudgetStatus.Valid, amounts[0], isBudgetOnly)
            : new(BudgetStatus.Ambiguous, IsBudgetOnly: isBudgetOnly);
    }

    private static decimal ConvertToRials(Match match)
    {
        var number = match.Groups["amount"].Value;
        number = GroupedNumber().IsMatch(number)
            ? number.Replace(",", "").Replace(".", "").Replace("٬", "")
            : number.Replace('٫', '.').Replace(',', '.');
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            throw new ArgumentException("مبلغ بودجه نامعتبر است.");
        }

        var multiplier = match.Groups["scale"].Value switch
        {
            "هزار" => 1_000m,
            "میلیون" => 1_000_000m,
            "میلیارد" => 1_000_000_000m,
            _ => 1m
        };
        if (match.Groups["currency"].Value is "تومان" or "تومن")
        {
            multiplier *= 10;
        }

        if (amount > MaximumAllowedRials / multiplier)
        {
            throw new ArgumentException("بودجه از سقف مجاز یک میلیارد ریال بیشتر است.");
        }

        return amount * multiplier;
    }
}
