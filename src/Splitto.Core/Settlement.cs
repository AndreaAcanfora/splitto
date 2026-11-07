namespace Splitto.Core;

/// <summary>A suggested payment that settles part of the debts.</summary>
public readonly record struct Transfer(Guid FromMemberId, Guid ToMemberId, long Cents);

/// <summary>
/// Finds the fewest payments that bring every balance to zero.
/// </summary>
/// <remarks>
/// <para>
/// With balances b₁…bₙ (positive: is owed money, negative: owes money, summing to zero), any group of
/// people whose balances sum to zero can settle among themselves with (size − 1) payments. So the fewest
/// payments overall is n − k, where k is the largest number of zero-sum groups the people can be split
/// into. Finding k is NP-hard (it contains subset sum), but for the group sizes of a trip or a flat it is
/// fast with dynamic programming over subsets: O(2ⁿ · n).
/// </para>
/// <para>
/// Above <see cref="ExactLimit"/> people with a non-zero balance the solver falls back to the classic
/// greedy (largest debtor pays largest creditor), which needs at most n − 1 payments.
/// </para>
/// </remarks>
public static class Settlement
{
    /// <summary>Largest number of non-zero balances solved exactly (2²⁰ states, a few milliseconds).</summary>
    public const int ExactLimit = 20;

    public static IReadOnlyList<Transfer> Minimize(IReadOnlyDictionary<Guid, long> balances)
    {
        ArgumentNullException.ThrowIfNull(balances);
        if (balances.Values.Sum() != 0)
        {
            throw new ArgumentException("Balances must add up to zero.", nameof(balances));
        }
        // Stable order (by id) so the same balances always give the same suggestion.
        var open = balances.Where(b => b.Value != 0).OrderBy(b => b.Key).ToList();
        if (open.Count == 0)
        {
            return [];
        }
        if (open.Count > ExactLimit)
        {
            return Greedy(open);
        }

        var groups = ZeroSumGroups(open.Select(b => b.Value).ToArray());
        var transfers = new List<Transfer>();
        foreach (var group in groups)
        {
            transfers.AddRange(Greedy(group.Select(i => open[i]).ToList()));
        }
        return transfers;
    }

    /// <summary>
    /// Splits indexes into the largest possible number of groups whose values sum to zero.
    /// dp[mask] = the most zero-sum groups that can be closed while adding the people in mask one by
    /// one; adding person i to mask either keeps the count or, if the new mask sums to zero, closes a group.
    /// </summary>
    private static List<List<int>> ZeroSumGroups(long[] values)
    {
        int n = values.Length;
        int full = (1 << n) - 1;
        var sum = new long[full + 1];
        var dp = new int[full + 1];
        var last = new int[full + 1]; // the person added last on the best path, to rebuild the order
        for (int mask = 1; mask <= full; mask++)
        {
            int low = System.Numerics.BitOperations.TrailingZeroCount(mask);
            sum[mask] = sum[mask & (mask - 1)] + values[low];
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) != 0 && dp[mask ^ (1 << i)] > best)
                {
                    best = dp[mask ^ (1 << i)];
                    last[mask] = i;
                }
            }
            dp[mask] = best + (sum[mask] == 0 ? 1 : 0);
        }

        // Walk back from everyone: the order people were added, cut wherever the running sum is zero.
        var order = new List<int>(n);
        for (int mask = full; mask != 0; mask ^= 1 << last[mask])
        {
            order.Add(last[mask]);
        }
        order.Reverse();
        var groups = new List<List<int>>();
        var current = new List<int>();
        long running = 0;
        foreach (int i in order)
        {
            current.Add(i);
            running += values[i];
            if (running == 0)
            {
                groups.Add(current);
                current = [];
            }
        }
        return groups;
    }

    /// <summary>Largest debtor pays the largest creditor, repeatedly: at most (count − 1) payments.</summary>
    private static List<Transfer> Greedy(IReadOnlyList<KeyValuePair<Guid, long>> people)
    {
        var creditors = new PriorityQueue<Guid, long>();
        var debtors = new PriorityQueue<Guid, long>();
        var remaining = new Dictionary<Guid, long>();
        foreach (var (id, balance) in people)
        {
            remaining[id] = balance;
            if (balance > 0)
            {
                creditors.Enqueue(id, -balance);
            }
            else if (balance < 0)
            {
                debtors.Enqueue(id, balance);
            }
        }
        var transfers = new List<Transfer>();
        while (creditors.Count > 0 && debtors.Count > 0)
        {
            Guid creditor = creditors.Dequeue();
            Guid debtor = debtors.Dequeue();
            long amount = Math.Min(remaining[creditor], -remaining[debtor]);
            transfers.Add(new Transfer(debtor, creditor, amount));
            remaining[creditor] -= amount;
            remaining[debtor] += amount;
            if (remaining[creditor] > 0)
            {
                creditors.Enqueue(creditor, -remaining[creditor]);
            }
            if (remaining[debtor] < 0)
            {
                debtors.Enqueue(debtor, remaining[debtor]);
            }
        }
        return transfers;
    }
}
