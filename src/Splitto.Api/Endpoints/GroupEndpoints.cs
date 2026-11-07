using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Splitto.Api.Data;
using Splitto.Core;
using Group = Splitto.Api.Data.Group;

namespace Splitto.Api.Endpoints;

/// <summary>
/// The HTTP API. A group is reached only through its secret token (the invite link), never by id, so
/// groups can't be enumerated.
/// </summary>
public static partial class GroupEndpoints
{
    private const long MaxCents = 100_000_000_00; // 100 million per expense is plenty

    public static void MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/groups");
        api.MapPost("/", CreateGroup).RequireRateLimiting("create-group");
        api.MapGet("/{token}", GetGroup);
        api.MapPost("/{token}/members", AddMember);
        api.MapGet("/{token}/expenses", ListExpenses);
        api.MapPost("/{token}/expenses", AddExpense);
        api.MapDelete("/{token}/expenses/{id:guid}", DeleteExpense);
        api.MapGet("/{token}/payments", ListPayments);
        api.MapPost("/{token}/payments", AddPayment);
        api.MapDelete("/{token}/payments/{id:guid}", DeletePayment);
        api.MapGet("/{token}/balances", GetBalances);
        api.MapGet("/{token}/export.csv", ExportCsv);
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Invalid(string message) =>
        TypedResults.Problem(message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid request");

    private static string? CleanName(string? name, int max) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > max ? null : name.Trim();

    private static Task<Group?> FindGroup(SplittoDb db, string token, CancellationToken ct) =>
        db.Groups.Include(g => g.Members.OrderBy(m => m.Name)).AsSplitQuery().FirstOrDefaultAsync(g => g.Token == token, ct);

    private static GroupDto ToDto(Group g) =>
        new(g.Token, g.Name, g.Currency, g.Members.OrderBy(m => m.Name).Select(m => new MemberDto(m.Id, m.Name)).ToList(), g.CreatedAt);

    private static string NewToken()
    {
        // 128 random bits, URL-safe: the link is the only key to the group.
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static async Task<IResult> CreateGroup(CreateGroupRequest request, SplittoDb db, TimeProvider clock, CancellationToken ct)
    {
        var name = CleanName(request.Name, 80);
        if (name is null)
        {
            return Invalid("Give the group a name (up to 80 characters).");
        }
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? "";
        if (!CurrencyCode().IsMatch(currency))
        {
            return Invalid("The currency must be a three-letter code such as EUR.");
        }
        var members = (request.Members ?? []).Select(m => CleanName(m, 60)).ToList();
        if (members.Count is < 1 or > 50 || members.Any(m => m is null))
        {
            return Invalid("Add between 1 and 50 people, each with a name of up to 60 characters.");
        }
        if (members.Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count)
        {
            return Invalid("Two people have the same name.");
        }
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Currency = currency,
            Token = NewToken(),
            CreatedAt = clock.GetUtcNow(),
            Members = members.Select(m => new Member { Id = Guid.CreateVersion7(), Name = m! }).ToList(),
        };
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/groups/{group.Token}", ToDto(group));
    }

    private static async Task<IResult> GetGroup(string token, SplittoDb db, CancellationToken ct) =>
        await FindGroup(db, token, ct) is { } group ? TypedResults.Ok(ToDto(group)) : TypedResults.NotFound();

    private static async Task<IResult> AddMember(string token, AddMemberRequest request, SplittoDb db, CancellationToken ct)
    {
        if (await FindGroup(db, token, ct) is not { } group)
        {
            return TypedResults.NotFound();
        }
        var name = CleanName(request.Name, 60);
        if (name is null)
        {
            return Invalid("Names are 1 to 60 characters.");
        }
        if (group.Members.Count >= 50)
        {
            return Invalid("A group can have at most 50 people.");
        }
        if (group.Members.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return Invalid($"{name} is already in the group.");
        }
        var member = new Member { Id = Guid.CreateVersion7(), GroupId = group.Id, Name = name };
        db.Members.Add(member);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/groups/{token}", new MemberDto(member.Id, member.Name));
    }

    private static ExpenseDto ToDto(Expense e) => new(
        e.Id, e.Description, e.PaidById, e.AmountCents, e.OriginalAmountCents, e.OriginalCurrency, e.ExchangeRate,
        e.Mode, e.Date, e.Shares.Select(s => new ShareDto(s.MemberId, s.Cents)).ToList());

    private static async Task<IResult> ListExpenses(string token, SplittoDb db, CancellationToken ct)
    {
        if (await FindGroup(db, token, ct) is not { } group)
        {
            return TypedResults.NotFound();
        }
        var expenses = await db.Expenses.Include(e => e.Shares).AsSplitQuery()
            .Where(e => e.GroupId == group.Id)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(expenses.Select(ToDto).ToList());
    }

    private static async Task<IResult> AddExpense(string token, CreateExpenseRequest request, SplittoDb db, TimeProvider clock, CancellationToken ct)
    {
        if (await FindGroup(db, token, ct) is not { } group)
        {
            return TypedResults.NotFound();
        }
        var description = CleanName(request.Description, 120);
        if (description is null)
        {
            return Invalid("Describe the expense (up to 120 characters).");
        }
        var memberIds = group.Members.Select(m => m.Id).ToHashSet();
        if (!memberIds.Contains(request.PaidById) || request.Participants.Any(p => !memberIds.Contains(p.MemberId)))
        {
            return Invalid("The payer and everyone in the split must be members of the group.");
        }
        if (request.AmountCents is <= 0 or > MaxCents)
        {
            return Invalid("The amount must be greater than zero.");
        }
        var currency = (request.Currency ?? group.Currency).Trim().ToUpperInvariant();
        if (!CurrencyCode().IsMatch(currency))
        {
            return Invalid("The currency must be a three-letter code such as USD.");
        }
        decimal rate = currency == group.Currency ? 1m : request.ExchangeRate ?? 0m;
        if (rate <= 0)
        {
            return Invalid($"Give the exchange rate from {currency} to {group.Currency}.");
        }

        IReadOnlyList<(Guid MemberId, long Cents)> parts;
        long amount;
        try
        {
            amount = Money.Convert(request.AmountCents, rate);
            parts = Splitter.Split(amount, request.Mode, request.Participants.Select(p => new SplitInput(p.MemberId, p.Value)).ToList());
        }
        catch (SplitException e)
        {
            return Invalid(e.Message);
        }

        var expense = new Expense
        {
            Id = Guid.CreateVersion7(),
            GroupId = group.Id,
            Description = description,
            PaidById = request.PaidById,
            AmountCents = amount,
            OriginalAmountCents = request.AmountCents,
            OriginalCurrency = currency,
            ExchangeRate = rate,
            Mode = request.Mode,
            Date = request.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            CreatedAt = clock.GetUtcNow(),
            // The expense and its shares are saved in one transaction: all or nothing.
            Shares = parts.Where(p => p.Cents > 0).Select(p => new ExpenseShare { MemberId = p.MemberId, Cents = p.Cents }).ToList(),
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/groups/{token}/expenses/{expense.Id}", ToDto(expense));
    }

    private static async Task<IResult> DeleteExpense(string token, Guid id, SplittoDb db, CancellationToken ct)
    {
        int deleted = await db.Expenses.Where(e => e.Id == id && db.Groups.Any(g => g.Id == e.GroupId && g.Token == token)).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<IResult> ListPayments(string token, SplittoDb db, CancellationToken ct)
    {
        if (await FindGroup(db, token, ct) is not { } group)
        {
            return TypedResults.NotFound();
        }
        var payments = await db.Payments.Where(p => p.GroupId == group.Id)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.CreatedAt)
            .Select(p => new PaymentDto(p.Id, p.FromId, p.ToId, p.AmountCents, p.Date))
            .ToListAsync(ct);
        return TypedResults.Ok(payments);
    }

    private static async Task<IResult> AddPayment(string token, CreatePaymentRequest request, SplittoDb db, TimeProvider clock, CancellationToken ct)
    {
        if (await FindGroup(db, token, ct) is not { } group)
        {
            return TypedResults.NotFound();
        }
        var memberIds = group.Members.Select(m => m.Id).ToHashSet();
        if (!memberIds.Contains(request.FromId) || !memberIds.Contains(request.ToId) || request.FromId == request.ToId)
        {
            return Invalid("A payment goes from one member of the group to another.");
        }
        if (request.AmountCents is <= 0 or > MaxCents)
        {
            return Invalid("The amount must be greater than zero.");
        }
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            GroupId = group.Id,
            FromId = request.FromId,
            ToId = request.ToId,
            AmountCents = request.AmountCents,
            Date = request.Date ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            CreatedAt = clock.GetUtcNow(),
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/groups/{token}/payments/{payment.Id}",
            new PaymentDto(payment.Id, payment.FromId, payment.ToId, payment.AmountCents, payment.Date));
    }

    private static async Task<IResult> DeletePayment(string token, Guid id, SplittoDb db, CancellationToken ct)
    {
        int deleted = await db.Payments.Where(p => p.Id == id && db.Groups.Any(g => g.Id == p.GroupId && g.Token == token)).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>
    /// Balances are computed in the database with GROUP BY queries (paid, owed, sent, received per
    /// member), then the settlement suggestion is computed in memory.
    /// </summary>
    private static async Task<IResult> GetBalances(string token, SplittoDb db, CancellationToken ct)
    {
        if (await FindGroup(db, token, ct) is not { } group)
        {
            return TypedResults.NotFound();
        }
        var paid = await db.Expenses.Where(e => e.GroupId == group.Id)
            .GroupBy(e => e.PaidById).Select(g => new { Id = g.Key, Cents = g.Sum(e => e.AmountCents) })
            .ToDictionaryAsync(x => x.Id, x => x.Cents, ct);
        var owed = await db.ExpenseShares.Where(s => db.Expenses.Any(e => e.Id == s.ExpenseId && e.GroupId == group.Id))
            .GroupBy(s => s.MemberId).Select(g => new { Id = g.Key, Cents = g.Sum(s => s.Cents) })
            .ToDictionaryAsync(x => x.Id, x => x.Cents, ct);
        var sent = await db.Payments.Where(p => p.GroupId == group.Id)
            .GroupBy(p => p.FromId).Select(g => new { Id = g.Key, Cents = g.Sum(p => p.AmountCents) })
            .ToDictionaryAsync(x => x.Id, x => x.Cents, ct);
        var received = await db.Payments.Where(p => p.GroupId == group.Id)
            .GroupBy(p => p.ToId).Select(g => new { Id = g.Key, Cents = g.Sum(p => p.AmountCents) })
            .ToDictionaryAsync(x => x.Id, x => x.Cents, ct);

        var balances = group.Members.Select(m =>
        {
            long p = paid.GetValueOrDefault(m.Id), o = owed.GetValueOrDefault(m.Id);
            long balance = p - o + sent.GetValueOrDefault(m.Id) - received.GetValueOrDefault(m.Id);
            return new BalanceDto(m.Id, p, o, balance);
        }).ToList();
        var transfers = Settlement.Minimize(balances.ToDictionary(b => b.MemberId, b => b.BalanceCents))
            .Select(t => new TransferDto(t.FromMemberId, t.ToMemberId, t.Cents)).ToList();
        return TypedResults.Ok(new BalancesDto(balances, transfers, paid.Values.Sum()));
    }

    private static async Task<IResult> ExportCsv(string token, SplittoDb db, CancellationToken ct)
    {
        if (await FindGroup(db, token, ct) is not { } group)
        {
            return TypedResults.NotFound();
        }
        var names = group.Members.ToDictionary(m => m.Id, m => m.Name);
        var members = group.Members.OrderBy(m => m.Name).ToList();
        var expenses = await db.Expenses.Include(e => e.Shares).AsSplitQuery()
            .Where(e => e.GroupId == group.Id).OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ToListAsync(ct);
        var payments = await db.Payments.Where(p => p.GroupId == group.Id).OrderBy(p => p.Date).ThenBy(p => p.CreatedAt).ToListAsync(ct);

        var csv = new StringBuilder();
        static string Cell(string value) =>
            value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        // One column per member with their share, so a spreadsheet can total them.
        csv.AppendLine(string.Join(',', new[] { "date", "type", "description", "paid by", $"amount ({group.Currency})", "original amount", "original currency" }
            .Concat(members.Select(m => $"share: {m.Name}")).Select(Cell)));
        foreach (var e in expenses)
        {
            var shares = e.Shares.ToDictionary(s => s.MemberId, s => s.Cents);
            csv.AppendLine(string.Join(',', new[]
            {
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "expense", e.Description, names[e.PaidById],
                Money.Format(e.AmountCents), Money.Format(e.OriginalAmountCents), e.OriginalCurrency,
            }.Concat(members.Select(m => Money.Format(shares.GetValueOrDefault(m.Id)))).Select(Cell)));
        }
        foreach (var p in payments)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "payment", $"{names[p.FromId]} paid {names[p.ToId]}",
                names[p.FromId], Money.Format(p.AmountCents), Money.Format(p.AmountCents), group.Currency,
            }.Concat(members.Select(_ => "")).Select(Cell)));
        }
        var fileName = Regex.Replace(group.Name, "[^A-Za-z0-9_-]+", "-").Trim('-');
        return TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", $"{(fileName.Length > 0 ? fileName : "splitto")}.csv");
    }
}
