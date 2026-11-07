namespace Splitto.Core;

// Data exchanged between the API and the Blazor client. Amounts are in cents of the group's currency
// unless stated otherwise.

public sealed record CreateGroupRequest(string Name, string Currency, IReadOnlyList<string> Members);

public sealed record MemberDto(Guid Id, string Name);

public sealed record GroupDto(string Token, string Name, string Currency, IReadOnlyList<MemberDto> Members, DateTimeOffset CreatedAt);

public sealed record AddMemberRequest(string Name);

public sealed record ShareInputDto(Guid MemberId, long Value);

/// <summary>
/// A new expense. <see cref="AmountCents"/> is in <see cref="Currency"/>; when that differs from the
/// group's currency, <see cref="ExchangeRate"/> converts it (1 unit of Currency = rate units of the group's).
/// For <see cref="SplitMode.Exact"/>, the values are in the group's currency.
/// </summary>
public sealed record CreateExpenseRequest(
    string Description,
    Guid PaidById,
    long AmountCents,
    SplitMode Mode,
    IReadOnlyList<ShareInputDto> Participants,
    DateOnly? Date = null,
    string? Currency = null,
    decimal? ExchangeRate = null);

public sealed record ShareDto(Guid MemberId, long Cents);

public sealed record ExpenseDto(
    Guid Id,
    string Description,
    Guid PaidById,
    long AmountCents,
    long OriginalAmountCents,
    string OriginalCurrency,
    decimal ExchangeRate,
    SplitMode Mode,
    DateOnly Date,
    IReadOnlyList<ShareDto> Shares);

public sealed record CreatePaymentRequest(Guid FromId, Guid ToId, long AmountCents, DateOnly? Date = null);

public sealed record PaymentDto(Guid Id, Guid FromId, Guid ToId, long AmountCents, DateOnly Date);

/// <summary>Positive: the member is owed money. Negative: the member owes money.</summary>
public sealed record BalanceDto(Guid MemberId, long PaidCents, long OwedCents, long BalanceCents);

public sealed record TransferDto(Guid FromId, Guid ToId, long Cents);

public sealed record BalancesDto(IReadOnlyList<BalanceDto> Balances, IReadOnlyList<TransferDto> Transfers, long TotalSpentCents);
