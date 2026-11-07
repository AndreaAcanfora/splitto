using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Splitto.Core;

namespace Splitto.Web.Services;

/// <summary>Typed access to the API. Errors come back as <see cref="ApiException"/> with the server's message.</summary>
public sealed class SplittoClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Task<GroupDto> CreateGroup(CreateGroupRequest request) => Send<GroupDto>(HttpMethod.Post, "api/groups", request);
    public Task<GroupDto> GetGroup(string token) => Send<GroupDto>(HttpMethod.Get, $"api/groups/{token}");
    public Task<MemberDto> AddMember(string token, string name) => Send<MemberDto>(HttpMethod.Post, $"api/groups/{token}/members", new AddMemberRequest(name));
    public Task<List<ExpenseDto>> Expenses(string token) => Send<List<ExpenseDto>>(HttpMethod.Get, $"api/groups/{token}/expenses");
    public Task<ExpenseDto> AddExpense(string token, CreateExpenseRequest request) => Send<ExpenseDto>(HttpMethod.Post, $"api/groups/{token}/expenses", request);
    public Task DeleteExpense(string token, Guid id) => Send<object>(HttpMethod.Delete, $"api/groups/{token}/expenses/{id}");
    public Task<List<PaymentDto>> Payments(string token) => Send<List<PaymentDto>>(HttpMethod.Get, $"api/groups/{token}/payments");
    public Task<PaymentDto> AddPayment(string token, CreatePaymentRequest request) => Send<PaymentDto>(HttpMethod.Post, $"api/groups/{token}/payments", request);
    public Task DeletePayment(string token, Guid id) => Send<object>(HttpMethod.Delete, $"api/groups/{token}/payments/{id}");
    public Task<BalancesDto> Balances(string token) => Send<BalancesDto>(HttpMethod.Get, $"api/groups/{token}/balances");

    private async Task<T> Send<T>(HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string message = response.StatusCode switch
            {
                System.Net.HttpStatusCode.NotFound => "This group doesn't exist, or the link is incomplete.",
                System.Net.HttpStatusCode.TooManyRequests => "Too many groups created from here: try again in a few minutes.",
                _ => "Something went wrong. Try again.",
            };
            try
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (problem.RootElement.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } text)
                {
                    message = text;
                }
            }
            catch (JsonException)
            {
                // not a problem+json body: keep the generic message
            }
            throw new ApiException(message);
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || typeof(T) == typeof(object))
        {
            return default!;
        }
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}

public sealed class ApiException(string message) : Exception(message);
