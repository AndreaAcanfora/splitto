using System.Text.Json;
using Microsoft.JSInterop;

namespace Splitto.Web.Services;

/// <summary>Groups opened on this device, kept in localStorage so they're one tap away from the home page.</summary>
public sealed class RecentGroups(IJSRuntime js)
{
    private const string Key = "splitto.recent";

    public sealed record Entry(string Token, string Name);

    public async Task<List<Entry>> List()
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            return json is null ? [] : JsonSerializer.Deserialize<List<Entry>>(json) ?? [];
        }
        catch (JSException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task Remember(string token, string name)
    {
        var entries = await List();
        entries.RemoveAll(e => e.Token == token);
        entries.Insert(0, new Entry(token, name));
        await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(entries.Take(8).ToList()));
    }
}
