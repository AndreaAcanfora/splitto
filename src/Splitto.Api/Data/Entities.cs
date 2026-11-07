using Splitto.Core;

namespace Splitto.Api.Data;

public sealed class Group
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>ISO 4217 code; all balances are in this currency.</summary>
    public required string Currency { get; set; }
    /// <summary>The secret in the invite link: whoever has it can see and edit the group.</summary>
    public required string Token { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Member> Members { get; set; } = [];
}

public sealed class Member
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public required string Name { get; set; }
}

public sealed class Expense
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public required string Description { get; set; }
    public Guid PaidById { get; set; }
    /// <summary>In the group's currency, after conversion.</summary>
    public long AmountCents { get; set; }
    public long OriginalAmountCents { get; set; }
    public required string OriginalCurrency { get; set; }
    public decimal ExchangeRate { get; set; }
    public SplitMode Mode { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ExpenseShare> Shares { get; set; } = [];
}

public sealed class ExpenseShare
{
    public Guid ExpenseId { get; set; }
    public Guid MemberId { get; set; }
    public long Cents { get; set; }
}

/// <summary>Money given from one member to another to settle up.</summary>
public sealed class Payment
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public long AmountCents { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
