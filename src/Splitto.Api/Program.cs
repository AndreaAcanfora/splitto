using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Splitto.Api.Data;
using Splitto.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SplittoDb>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Splitto")
        ?? throw new InvalidOperationException("Set ConnectionStrings__Splitto to a PostgreSQL connection string.")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
// Creating groups is the only unauthenticated write that costs storage: limit it per client address.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("create-group", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SplittoDb>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

// The Blazor WebAssembly client is served by this same app.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.MapGroupEndpoints();
app.MapGet("/healthz", () => "ok");
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>Entry point, public so integration tests can start the app with WebApplicationFactory.</summary>
public partial class Program;
