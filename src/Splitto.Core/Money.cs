using System.Globalization;

namespace Splitto.Core;

/// <summary>Money is stored as whole cents (<see cref="long"/>), never as floating point.</summary>
public static class Money
{
    /// <summary>Converts a decimal amount like 12.34 to cents, rejecting fractions of a cent.</summary>
    public static long ToCents(decimal amount)
    {
        decimal cents = amount * 100m;
        if (cents != decimal.Truncate(cents))
        {
            throw new SplitException("Amounts can have at most two decimals.");
        }
        return (long)cents;
    }

    /// <summary>Converts an amount in another currency: cents × rate, rounded half away from zero.</summary>
    public static long Convert(long cents, decimal rate)
    {
        if (rate <= 0)
        {
            throw new SplitException("The exchange rate must be greater than zero.");
        }
        return (long)decimal.Round(cents * rate, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Formats cents as "12.34" (invariant culture; the UI adds the currency).</summary>
    public static string Format(long cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
