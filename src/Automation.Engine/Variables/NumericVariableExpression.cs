using System.Globalization;

namespace Automation.Engine.Variables;

public static class NumericVariableExpression
{
    public static bool TryApply(string? expression, long current, out long result)
    {
        result = current;
        var text = expression?.Trim().Replace('×', 'x') ?? "";
        if (text.Length == 0) return false;
        try
        {
            if (text.StartsWith('='))
                return long.TryParse(text[1..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            if (text.StartsWith('x') || text.StartsWith('*'))
            {
                if (!decimal.TryParse(text[1..].Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var factor)) return false;
                result = checked((long)Math.Round(current * factor, MidpointRounding.AwayFromZero));
                return true;
            }
            if (text.StartsWith('/'))
            {
                if (!decimal.TryParse(text[1..].Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var divisor) || divisor == 0) return false;
                result = checked((long)Math.Round(current / divisor, MidpointRounding.AwayFromZero));
                return true;
            }
            if (text.StartsWith('+') || text.StartsWith('-'))
            {
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta)) return false;
                result = checked(current + delta);
                return true;
            }
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }
        catch (OverflowException) { return false; }
    }
}
