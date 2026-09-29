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
    public decimal? MinimumPriceRials { get; init; }
    public bool NeedsClarification => Status is BudgetStatus.MissingCurrency or BudgetStatus.Ambiguous;

    public string? FollowUpQuestion => Status switch
    {
        BudgetStatus.MissingCurrency => "مبلغ بودجه را با واحد ریال یا تومان می‌فرمایید؟",
        BudgetStatus.Ambiguous => "مبلغ‌ها یا بازهٔ قیمتی با هم سازگار نیستند؛ لطفاً حداقل و حداکثر بودجه را مشخص کنید.",
        _ => null
    };
}

// Amounts entering catalog filters always use the database unit: IRR.
public static partial class BudgetParser
{
    public const decimal MaximumAllowedRials = 1_000_000_000m;

    private const string Amount = @"(?<amount>[+-]?[0-9]+(?:[.,٫٬][0-9]+)*)\s*(?<scale>هزار|میلیون|میلیارد)?\s*(?<currency>تومان|تومن|ریال)?(?![\p{L}\p{N}])";

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:بین|از)\s*(?<lowAmount>[+-]?[0-9]+(?:[.,٫٬][0-9]+)*)\s*(?<lowScale>هزار|میلیون|میلیارد)?\s*(?<lowCurrency>تومان|تومن|ریال)?\s*(?:تا|الی)\s*(?<highAmount>[+-]?[0-9]+(?:[.,٫٬][0-9]+)*)\s*(?<highScale>هزار|میلیون|میلیارد)?\s*(?<highCurrency>تومان|تومن|ریال)?(?![\p{L}\p{N}])")]
    private static partial Regex BudgetRange();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:تا|زیر|حداکثر|بودجه(?:\s+(?:من|ام))?(?:\s+(?:تا|است|هست|حدود))?|سقف(?:\s+قیمت)?|با\s+بودجه)\s*(?:قیمت\s*)?" + Amount)]
    private static partial Regex BudgetLimit();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])من\s+(?:(?:فقط|حدود|تقریبا)\s+)*" + Amount + @"\s*(?:پول\s+)?دارم\b")]
    private static partial Regex AvailableMoney();

    [GeneratedRegex(@"^" + Amount + @"(?:\s+(?:دارم|پول دارم|بودجه دارم))?\s*[.!؟?]*$")]
    private static partial Regex StandaloneAmount();

    [GeneratedRegex(@"^(?:(?:سلام|درود|رفیق|من|فقط|حدود|تقریبا|ممنون|بین|از)[\s،,!؟?]*)*$")]
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
        var range = BudgetRange().Match(text);
        if (range.Success)
        {
            var lowCurrency = range.Groups["lowCurrency"].Value;
            var highCurrency = range.Groups["highCurrency"].Value;
            var lowScale = range.Groups["lowScale"].Value;
            var highScale = range.Groups["highScale"].Value;
            lowCurrency = lowCurrency.Length == 0 ? highCurrency : lowCurrency;
            highCurrency = highCurrency.Length == 0 ? lowCurrency : highCurrency;
            lowScale = lowScale.Length == 0 ? highScale : lowScale;
            highScale = highScale.Length == 0 ? lowScale : highScale;
            var rangeIsBudgetOnly = SocialPrefix().IsMatch(text[..range.Index].Trim())
                && BudgetSuffix().IsMatch(text[(range.Index + range.Length)..].Trim());
            if (lowCurrency.Length == 0 || highCurrency.Length == 0)
            {
                return new(BudgetStatus.MissingCurrency, IsBudgetOnly: rangeIsBudgetOnly);
            }

            var minimum = ConvertToRials(range.Groups["lowAmount"].Value, lowScale, lowCurrency);
            var maximum = ConvertToRials(range.Groups["highAmount"].Value, highScale, highCurrency);
            return minimum <= maximum
                ? new(BudgetStatus.Valid, maximum, rangeIsBudgetOnly) { MinimumPriceRials = minimum }
                : new(BudgetStatus.Ambiguous, IsBudgetOnly: rangeIsBudgetOnly);
        }

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

        var firstMatch = matches.MinBy(m => m.Index)!;
        var lastMatch = matches.MaxBy(m => m.Index + m.Length)!;
        var isBudgetOnly = SocialPrefix().IsMatch(text[..firstMatch.Index].Trim())
            && BudgetSuffix().IsMatch(text[(lastMatch.Index + lastMatch.Length)..].Trim());
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
        return ConvertToRials(match.Groups["amount"].Value, match.Groups["scale"].Value, match.Groups["currency"].Value);
    }

    private static decimal ConvertToRials(string value, string scale, string currency)
    {
        var number = value;
        if (number.Contains('٫'))
        {
            if (number.Contains(',') || number.Contains('.') || number.Contains('٬'))
                throw new ArgumentException("جداکننده‌های مبلغ را با یک قالب یکسان بنویسید.");
            number = number.Replace('٫', '.');
        }
        else if (number.Contains('٬'))
        {
            if (number.Contains(',') || number.Contains('.') || !GroupedNumber().IsMatch(number))
                throw new ArgumentException("قالب جداکنندهٔ هزارگان مبلغ معتبر نیست.");
            number = number.Replace("٬", "");
        }
        else if (number.Contains(',') || number.Contains('.'))
        {
            var separator = number.Contains(',') ? ',' : '.';
            if (number.Contains(',') && number.Contains('.'))
                throw new ArgumentException("جداکننده‌های مبلغ را با یک قالب یکسان بنویسید.");
            if (GroupedNumber().IsMatch(number))
                number = number.Replace(separator.ToString(), "");
            else if (number.Count(c => c == separator) > 1)
                throw new ArgumentException("قالب جداکنندهٔ مبلغ معتبر نیست.");
            else
                number = number.Replace(separator, '.');
        }
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            throw new ArgumentException("مبلغ بودجه نامعتبر است.");
        }

        var multiplier = scale switch
        {
            "هزار" => 1_000m,
            "میلیون" => 1_000_000m,
            "میلیارد" => 1_000_000_000m,
            _ => 1m
        };
        if (currency is "تومان" or "تومن")
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
