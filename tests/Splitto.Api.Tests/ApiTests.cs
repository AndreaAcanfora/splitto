using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Splitto.Api.Data;
using Splitto.Core;

namespace Splitto.Api.Tests;

public sealed class ApiTests(SplittoFactory factory) : IClassFixture<SplittoFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly HttpClient _http = factory.CreateClient();

    private async Task<GroupDto> CreateGroup(string currency = "EUR", params string[] members)
    {
        var response = await _http.PostAsJsonAsync("/api/groups", new CreateGroupRequest("Trip", currency, members.Length > 0 ? members : ["Andrea", "Giulia", "Marco", "Sara"]), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GroupDto>(Json))!;
    }

    private static Guid Id(GroupDto group, string name) => group.Members.Single(m => m.Name == name).Id;

    private static List<ShareInputDto> Everyone(GroupDto group) => group.Members.Select(m => new ShareInputDto(m.Id, 1)).ToList();

    private async Task<HttpResponseMessage> AddExpense(GroupDto group, CreateExpenseRequest request) =>
        await _http.PostAsJsonAsync($"/api/groups/{group.Token}/expenses", request, Json);

    private async Task<BalancesDto> Balances(GroupDto group) =>
        (await _http.GetFromJsonAsync<BalancesDto>($"/api/groups/{group.Token}/balances", Json))!;

    private static async Task<string> Problem(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("detail").GetString()!;
    }

    [Fact]
    public async Task ATripFromFirstExpenseToSettledUp()
    {
        var group = await CreateGroup();
        Assert.Equal(22, group.Token.Length);
        (await AddExpense(group, new("Fuel", Id(group, "Andrea"), 12000, SplitMode.Equal, Everyone(group)))).EnsureSuccessStatusCode();
        (await AddExpense(group, new("Dinner", Id(group, "Giulia"), 15550, SplitMode.Shares,
            group.Members.Select(m => new ShareInputDto(m.Id, m.Name == "Giulia" ? 2 : 1)).ToList()))).EnsureSuccessStatusCode();
        (await AddExpense(group, new("Boat", Id(group, "Marco"), 20000, SplitMode.Equal, Everyone(group), Currency: "USD", ExchangeRate: 0.92m))).EnsureSuccessStatusCode();

        var balances = await Balances(group);
        Assert.Equal(12000 + 15550 + 18400, balances.TotalSpentCents);
        Assert.Equal(0, balances.Balances.Sum(b => b.BalanceCents));
        Assert.Equal(-10710 + 12000, balances.Balances.Single(b => b.MemberId == Id(group, "Andrea")).BalanceCents);

        // Pay every suggested transfer: everyone ends at zero and nothing more is suggested.
        foreach (var transfer in balances.Transfers)
        {
            var paid = await _http.PostAsJsonAsync($"/api/groups/{group.Token}/payments", new CreatePaymentRequest(transfer.FromId, transfer.ToId, transfer.Cents), Json);
            Assert.Equal(HttpStatusCode.Created, paid.StatusCode);
        }
        var settled = await Balances(group);
        Assert.All(settled.Balances, b => Assert.Equal(0, b.BalanceCents));
        Assert.Empty(settled.Transfers);
    }

    [Fact]
    public async Task DeletingAnExpenseUpdatesBalances()
    {
        var group = await CreateGroup();
        var created = await AddExpense(group, new("Taxi", Id(group, "Sara"), 4000, SplitMode.Equal, Everyone(group)));
        var expense = (await created.Content.ReadFromJsonAsync<ExpenseDto>(Json))!;
        Assert.Equal(3000, (await Balances(group)).Balances.Single(b => b.MemberId == Id(group, "Sara")).BalanceCents);
        Assert.Equal(HttpStatusCode.NoContent, (await _http.DeleteAsync($"/api/groups/{group.Token}/expenses/{expense.Id}")).StatusCode);
        Assert.All((await Balances(group)).Balances, b => Assert.Equal(0, b.BalanceCents));
        Assert.Empty(await _http.GetFromJsonAsync<List<ExpenseDto>>($"/api/groups/{group.Token}/expenses", Json) ?? []);
    }

    [Fact]
    public async Task ValidationErrorsExplainWhatToFix()
    {
        Assert.Equal("The currency must be a three-letter code such as EUR.",
            await Problem(await _http.PostAsJsonAsync("/api/groups", new CreateGroupRequest("Trip", "euro", ["A"]), Json)));
        Assert.Equal("Two people have the same name.",
            await Problem(await _http.PostAsJsonAsync("/api/groups", new CreateGroupRequest("Trip", "EUR", ["Ada", "ada"]), Json)));
        Assert.Equal("Give the group a name (up to 80 characters).",
            await Problem(await _http.PostAsJsonAsync("/api/groups", new CreateGroupRequest(" ", "EUR", ["A"]), Json)));

        var group = await CreateGroup();
        var stranger = Guid.NewGuid();
        Assert.Equal("The payer and everyone in the split must be members of the group.",
            await Problem(await AddExpense(group, new("X", stranger, 100, SplitMode.Equal, Everyone(group)))));
        Assert.Equal("The amounts add up to 1.00, not 10.00.",
            await Problem(await AddExpense(group, new("X", Id(group, "Sara"), 1000, SplitMode.Exact, [new(Id(group, "Sara"), 100)]))));
        Assert.Equal("Give the exchange rate from GBP to EUR.",
            await Problem(await AddExpense(group, new("X", Id(group, "Sara"), 1000, SplitMode.Equal, Everyone(group), Currency: "GBP"))));
        Assert.Equal("Sara is already in the group.",
            await Problem(await _http.PostAsJsonAsync($"/api/groups/{group.Token}/members", new AddMemberRequest(" Sara "), Json)));
        Assert.Equal("A payment goes from one member of the group to another.",
            await Problem(await _http.PostAsJsonAsync($"/api/groups/{group.Token}/payments", new CreatePaymentRequest(Id(group, "Sara"), Id(group, "Sara"), 100), Json)));
    }

    [Fact]
    public async Task GroupsAreReachableOnlyWithTheirToken()
    {
        var mine = await CreateGroup();
        var theirs = await CreateGroup();
        var created = await AddExpense(theirs, new("Secret", Id(theirs, "Sara"), 500, SplitMode.Equal, Everyone(theirs)));
        var expense = (await created.Content.ReadFromJsonAsync<ExpenseDto>(Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/groups/not-a-real-token")).StatusCode);
        // Knowing an expense id is not enough: it must be deleted through its own group's link.
        Assert.Equal(HttpStatusCode.NotFound, (await _http.DeleteAsync($"/api/groups/{mine.Token}/expenses/{expense.Id}")).StatusCode);
        Assert.Single(await _http.GetFromJsonAsync<List<ExpenseDto>>($"/api/groups/{theirs.Token}/expenses", Json) ?? []);
        Assert.NotEqual(mine.Token, theirs.Token);
    }

    [Fact]
    public async Task TheDatabaseRejectsBadDataEvenIfTheApiWereBypassed()
    {
        var group = await CreateGroup();
        await using var db = factory.CreateDb();
        var groupId = await db.Groups.Where(g => g.Token == group.Token).Select(g => g.Id).SingleAsync();
        db.Payments.Add(new Payment { Id = Guid.NewGuid(), GroupId = groupId, FromId = Id(group, "Sara"), ToId = Id(group, "Sara"), AmountCents = -5 });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_Payments", error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportsAQuotedCsvWithOneColumnPerMember()
    {
        var group = await CreateGroup("EUR", "Ada", "Bob");
        await AddExpense(group, new("Pizza, beer \"and more\"", Id(group, "Ada"), 2001, SplitMode.Equal, Everyone(group)));
        var response = await _http.GetAsync($"/api/groups/{group.Token}/export.csv");
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var lines = (await response.Content.ReadAsStringAsync()).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(l => l.Split('\n', StringSplitOptions.RemoveEmptyEntries)).ToList();
        Assert.Equal("date,type,description,paid by,amount (EUR),original amount,original currency,share: Ada,share: Bob", lines[0]);
        Assert.EndsWith(",expense,\"Pizza, beer \"\"and more\"\"\",Ada,20.01,20.01,EUR,10.01,10.00", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task MembersCanJoinLater()
    {
        var group = await CreateGroup("EUR", "Ada");
        var response = await _http.PostAsJsonAsync($"/api/groups/{group.Token}/members", new AddMemberRequest("Bob"), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updated = await _http.GetFromJsonAsync<GroupDto>($"/api/groups/{group.Token}", Json);
        Assert.Equal(["Ada", "Bob"], updated!.Members.Select(m => m.Name));
    }
}
