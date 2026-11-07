using Splitto.Core;

namespace Splitto.Core.Tests;

public class SplitterTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    private static long[] Cents(IReadOnlyList<(Guid, long Cents)> parts) => parts.Select(p => p.Cents).ToArray();

    [Fact]
    public void EqualSplitGivesLeftoverCentsInOrder()
    {
        var parts = Splitter.Split(1000, SplitMode.Equal, [new(A), new(B), new(C)]);
        Assert.Equal([334, 333, 333], Cents(parts));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(100, 3)]
    [InlineData(99_999, 7)]
    [InlineData(1_000_000_001, 13)]
    public void PartsAlwaysAddUpToTheTotal(long total, int people)
    {
        var participants = Enumerable.Range(0, people).Select(_ => new SplitInput(Guid.NewGuid())).ToList();
        var parts = Splitter.Split(total, SplitMode.Equal, participants);
        Assert.Equal(total, parts.Sum(p => p.Cents));
        Assert.True(parts.Max(p => p.Cents) - parts.Min(p => p.Cents) <= 1);
    }

    [Fact]
    public void SharesAreProportional()
    {
        // A couple counts as two: 90.00 split 2:1 is 60.00 and 30.00.
        var parts = Splitter.Split(9000, SplitMode.Shares, [new(A, 2), new(B, 1)]);
        Assert.Equal([6000, 3000], Cents(parts));
        var uneven = Splitter.Split(100, SplitMode.Shares, [new(A, 1), new(B, 1), new(C, 1)]);
        Assert.Equal(100, uneven.Sum(p => p.Cents));
        var withZero = Splitter.Split(500, SplitMode.Shares, [new(A, 0), new(B, 1)]);
        Assert.Equal([0, 500], Cents(withZero));
    }

    [Fact]
    public void ExactAmountsMustAddUp()
    {
        Assert.Equal([700, 300], Cents(Splitter.Split(1000, SplitMode.Exact, [new(A, 700), new(B, 300)])));
        var error = Assert.Throws<SplitException>(() => Splitter.Split(1000, SplitMode.Exact, [new(A, 700), new(B, 200)]));
        Assert.Equal("The amounts add up to 9.00, not 10.00.", error.Message);
    }

    [Fact]
    public void RejectsInvalidSplits()
    {
        Assert.Throws<SplitException>(() => Splitter.Split(0, SplitMode.Equal, [new(A)]));
        Assert.Throws<SplitException>(() => Splitter.Split(100, SplitMode.Equal, []));
        Assert.Throws<SplitException>(() => Splitter.Split(100, SplitMode.Equal, [new(A), new(A)]));
        Assert.Throws<SplitException>(() => Splitter.Split(100, SplitMode.Shares, [new(A, 0), new(B, 0)]));
        Assert.Throws<SplitException>(() => Splitter.Split(100, SplitMode.Shares, [new(A, -1), new(B, 2)]));
    }

    [Fact]
    public void MoneyConversions()
    {
        Assert.Equal(1234, Money.ToCents(12.34m));
        Assert.Throws<SplitException>(() => Money.ToCents(1.234m));
        Assert.Equal(1086, Money.Convert(1000, 1.0855m)); // 10.00 USD at 1.0855 → 10.86 (rounded)
        Assert.Equal(5, Money.Convert(10, 0.45m)); // 4.5 rounds away from zero
        Assert.Throws<SplitException>(() => Money.Convert(100, 0));
        Assert.Equal("-3.50", Money.Format(-350));
    }
}
