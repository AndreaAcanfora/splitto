using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Splitto.Api.Data;

namespace Splitto.Api.Tests;

/// <summary>
/// Starts the real app against a real PostgreSQL, with a fresh database per test class (created by the
/// app's own migrations, dropped afterwards). Set SPLITTO_TEST_DB to point at a server; the default is a
/// local one on port 5433. CI runs PostgreSQL as a service container.
/// </summary>
public sealed class SplittoFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _database = $"splitto_test_{Guid.NewGuid():N}";

    private static string DatabaseServer =>
        Environment.GetEnvironmentVariable("SPLITTO_TEST_DB") ?? "Host=127.0.0.1;Port=5433;Username=splitto";

    public string ConnectionString => $"{DatabaseServer};Database={_database}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Splitto", ConnectionString);
    }

    public SplittoDb CreateDb() => Services.CreateScope().ServiceProvider.GetRequiredService<SplittoDb>();

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection($"{DatabaseServer};Database=postgres");
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
