namespace Splitto.Core;

/// <summary>How an expense is divided among the people it is for.</summary>
public enum SplitMode
{
    /// <summary>Everyone pays the same.</summary>
    Equal,
    /// <summary>Proportional to weights: 2 shares pays twice as much as 1.</summary>
    Shares,
    /// <summary>Exact amounts, which must add up to the total.</summary>
    Exact,
}

/// <summary>One participant's input to a split: a weight for <see cref="SplitMode.Shares"/>,
/// an amount in cents for <see cref="SplitMode.Exact"/>, ignored for <see cref="SplitMode.Equal"/>.</summary>
public readonly record struct SplitInput(Guid MemberId, long Value = 1);

/// <summary>Divides amounts in cents so the parts always add up to the total exactly.</summary>
public static class Splitter
{
    /// <summary>
    /// Splits <paramref name="totalCents"/> among the participants. Cents that don't divide evenly go to
    /// the participants with the largest remainders, ties broken by input order, so the result is
    /// deterministic: 10.00 among three is 3.34, 3.33, 3.33.
    /// </summary>
    public static IReadOnlyList<(Guid MemberId, long Cents)> Split(long totalCents, SplitMode mode, IReadOnlyList<SplitInput> participants)
    {
        ArgumentNullException.ThrowIfNull(participants);
        if (totalCents <= 0)
        {
            throw new SplitException("The amount must be greater than zero.");
        }
        if (participants.Count == 0)
        {
            throw new SplitException("Choose at least one person to split with.");
        }
        if (participants.Select(p => p.MemberId).Distinct().Count() != participants.Count)
        {
            throw new SplitException("Each person can appear only once in a split.");
        }

        return mode switch
        {
            SplitMode.Equal => Proportional(totalCents, participants.Select(p => p with { Value = 1 }).ToList()),
            SplitMode.Shares => Proportional(totalCents, participants),
            SplitMode.Exact => Exact(totalCents, participants),
            _ => throw new SplitException($"Unknown split mode {mode}."),
        };
    }

    private static List<(Guid, long)> Proportional(long totalCents, IReadOnlyList<SplitInput> participants)
    {
        if (participants.Any(p => p.Value < 0) || participants.All(p => p.Value == 0))
        {
            throw new SplitException("Shares must be zero or more, and at least one must be positive.");
        }
        long totalWeight = participants.Sum(p => p.Value);
        var parts = participants
            .Select((p, index) =>
            {
                // Integer maths: floor of the exact share, and the remainder to rank who gets leftover cents.
                Int128 numerator = (Int128)totalCents * p.Value;
                return (p.MemberId, Index: index, Cents: (long)(numerator / totalWeight), Remainder: (long)(numerator % totalWeight));
            })
            .ToList();
        long leftover = totalCents - parts.Sum(p => p.Cents);
        foreach (var part in parts.OrderByDescending(p => p.Remainder).ThenBy(p => p.Index).Take((int)leftover).ToList())
        {
            parts[part.Index] = part with { Cents = part.Cents + 1 };
        }
        return parts.Select(p => (p.MemberId, p.Cents)).ToList();
    }

    private static List<(Guid, long)> Exact(long totalCents, IReadOnlyList<SplitInput> participants)
    {
        if (participants.Any(p => p.Value < 0))
        {
            throw new SplitException("Amounts can't be negative.");
        }
        long sum = participants.Sum(p => p.Value);
        if (sum != totalCents)
        {
            throw new SplitException($"The amounts add up to {Money.Format(sum)}, not {Money.Format(totalCents)}.");
        }
        return participants.Select(p => (p.MemberId, p.Value)).ToList();
    }
}

/// <summary>A split that can't be done as asked; the message is meant for the user.</summary>
public sealed class SplitException(string message) : Exception(message);
