using Splitto.Core;

namespace Splitto.Core.Tests;

public class SettlementTests
{
    private static Dictionary<Guid, long> Balances(params long[] values) =>
        values.ToDictionary(_ => Guid.NewGuid(), v => v);

    /// <summary>Applies the transfers and checks that everyone ends at zero.</summary>
    private static void AssertSettles(IReadOnlyDictionary<Guid, long> balances, IReadOnlyList<Transfer> transfers)
    {
        var after = balances.ToDictionary(b => b.Key, b => b.Value);
        foreach (var t in transfers)
        {
            Assert.True(t.Cents > 0, "transfers move a positive amount");
            Assert.NotEqual(t.FromMemberId, t.ToMemberId);
            after[t.FromMemberId] += t.Cents;
            after[t.ToMemberId] -= t.Cents;
        }
        Assert.All(after.Values, v => Assert.Equal(0, v));
    }

    /// <summary>The true minimum by brute force: n − (most zero-sum groups the values split into).</summary>
    private static int BruteForceMinimum(long[] values)
    {
        var open = values.Where(v => v != 0).ToArray();
        return open.Length - MostZeroSumGroups(open.ToList());

        static int MostZeroSumGroups(List<long> items)
        {
            if (items.Count == 0)
            {
                return 0;
            }
            // The first item belongs to some zero-sum subset of the items: try them all.
            int best = 0;
            var rest = items.Skip(1).ToList();
            for (int mask = 0; mask < 1 << rest.Count; mask++)
            {
                long sum = items[0];
                var remaining = new List<long>();
                for (int i = 0; i < rest.Count; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        sum += rest[i];
                    }
                    else
                    {
                        remaining.Add(rest[i]);
                    }
                }
                if (sum == 0)
                {
                    best = Math.Max(best, 1 + MostZeroSumGroups(remaining));
                }
            }
            return best;
        }
    }

    [Fact]
    public void NothingToDoWhenEveryoneIsEven()
    {
        Assert.Empty(Settlement.Minimize(Balances(0, 0, 0)));
        Assert.Empty(Settlement.Minimize(new Dictionary<Guid, long>()));
    }

    [Fact]
    public void OneDebtorOneCreditor()
    {
        var balances = Balances(2500, -2500);
        var transfers = Settlement.Minimize(balances);
        var transfer = Assert.Single(transfers);
        Assert.Equal(2500, transfer.Cents);
        AssertSettles(balances, transfers);
    }

    [Fact]
    public void BeatsTheGreedyWhenSubgroupsCanSettleAmongThemselves()
    {
        // Largest creditor paid by largest debtor needs 4 payments here; the optimum is 3:
        // {+3, −3} settle with one payment and {+4, −2, −2} with two.
        var balances = Balances(400, 300, -200, -200, -300);
        var transfers = Settlement.Minimize(balances);
        Assert.Equal(3, transfers.Count);
        AssertSettles(balances, transfers);
    }

    [Fact]
    public void MatchesTheBruteForceOptimumOnRandomGroups()
    {
        var random = new Random(20261107);
        for (int round = 0; round < 400; round++)
        {
            int people = random.Next(2, 9);
            // Small amounts make zero-sum subgroups common, which is where the optimisation matters.
            var values = Enumerable.Range(0, people - 1).Select(_ => (long)random.Next(-6, 7) * 100).ToList();
            values.Add(-values.Sum());
            var balances = values.ToDictionary(_ => Guid.NewGuid(), v => v);
            var transfers = Settlement.Minimize(balances);
            AssertSettles(balances, transfers);
            Assert.Equal(BruteForceMinimum([.. values]), transfers.Count);
        }
    }

    [Fact]
    public void LargeGroupsFallBackToGreedyAndStillSettle()
    {
        var random = new Random(7);
        var values = Enumerable.Range(0, 59).Select(_ => (long)random.Next(-50_000, 50_000)).ToList();
        values.Add(-values.Sum());
        var balances = values.ToDictionary(_ => Guid.NewGuid(), v => v);
        var transfers = Settlement.Minimize(balances);
        AssertSettles(balances, transfers);
        Assert.True(transfers.Count <= balances.Count(b => b.Value != 0) - 1);
    }

    [Fact]
    public void TwentyPeopleAreSolvedExactlyAndQuickly()
    {
        var random = new Random(3);
        var values = Enumerable.Range(0, 19).Select(_ => (long)random.Next(-9, 10) * 1000).Where(v => v != 0).ToList();
        values.Add(-values.Sum());
        var balances = values.ToDictionary(_ => Guid.NewGuid(), v => v);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var transfers = Settlement.Minimize(balances);
        watch.Stop();
        AssertSettles(balances, transfers);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void SameBalancesSameSuggestion()
    {
        var balances = Balances(1000, -400, -600);
        Assert.Equal(Settlement.Minimize(balances), Settlement.Minimize(balances));
    }

    [Fact]
    public void RejectsBalancesThatDoNotAddUp()
    {
        Assert.Throws<ArgumentException>(() => Settlement.Minimize(Balances(100, -50)));
    }
}
