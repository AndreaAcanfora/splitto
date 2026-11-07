using System.Globalization;
using Splitto.Core;

namespace Splitto.Web.Services;

public static class Format
{
    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["EUR"] = "€", ["USD"] = "$", ["GBP"] = "£", ["CHF"] = "CHF ", ["JPY"] = "¥", ["SEK"] = "kr ", ["NOK"] = "kr ", ["DKK"] = "kr ",
    };

    /// <summary>€12.34, -€3.50.</summary>
    public static string Money(long cents, string currency)
    {
        string symbol = Symbols.GetValueOrDefault(currency, currency + " ");
        string amount = Core.Money.Format(Math.Abs(cents));
        return cents < 0 ? $"-{symbol}{amount}" : $"{symbol}{amount}";
    }

    /// <summary>Reads "12.34", "12,34" or "1,234.50" typed by the user into cents.</summary>
    public static bool TryParseCents(string? text, out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string normalized = text.Trim().Replace(" ", "", StringComparison.Ordinal);
        int comma = normalized.LastIndexOf(',');
        int dot = normalized.LastIndexOf('.');
        if (comma > dot)
        {
            // "1.234,50" or "12,5": the comma is the decimal separator.
            normalized = normalized.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        }
        else
        {
            normalized = normalized.Replace(",", "", StringComparison.Ordinal);
        }
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) || value <= 0)
        {
            return false;
        }
        try
        {
            cents = Core.Money.ToCents(value);
            return true;
        }
        catch (SplitException)
        {
            return false;
        }
    }
}
