using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WinLaunch.Enhanced.Services;

// Quick arithmetic in the search box: digits, + - * / %, parentheses and
// decimals. Anything else is not treated as math, so app names never match.
public static class Calculator
{
    private static readonly Regex Allowed = new(@"^[\d\s\.\+\-\*/%\(\),x\u00D7\u00F7]+$", RegexOptions.Compiled);
    private static readonly Regex HasOperator = new(@"\d\s*[\+\-\*/%x\u00D7\u00F7]\s*[\d\(]", RegexOptions.Compiled);

    public static string TryEvaluate(string input)
    {
        var text = (input ?? "").Trim();
        if (text.Length < 3 || text.Length > 120 || !Allowed.IsMatch(text) || !HasOperator.IsMatch(text))
            return null;
        text = text.Replace('x', '*').Replace('\u00D7', '*').Replace('\u00F7', '/').Replace(",", "");
        try
        {
            var value = Convert.ToDouble(new DataTable().Compute(text, null), CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value))
                return null;
            return Math.Abs(value) >= 1e15 || (value != 0 && Math.Abs(value) < 1e-6)
                ? value.ToString("G10", CultureInfo.InvariantCulture)
                : Math.Round(value, 10).ToString("#,0.##########", CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
