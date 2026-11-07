using System.Net;
using System.Net.Http.Json;
using Splitto.Core;

namespace Splitto.Api.Tests;

public sealed class RateLimitTests(SplittoFactory factory) : IClassFixture<SplittoFactory>
{
    [Fact]
    public async Task CreatingGroupsIsRateLimited()
    {
        var http = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 21; i++)
        {
            statuses.Add((await http.PostAsJsonAsync("/api/groups", new CreateGroupRequest($"G{i}", "EUR", ["A"]))).StatusCode);
        }
        Assert.All(statuses.Take(20), s => Assert.Equal(HttpStatusCode.Created, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[20]);
    }
}
