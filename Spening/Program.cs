using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Default is required. Set it via environment variable " +
        "ConnectionStrings__Default.");

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// ── Blazor + API ──────────────────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();

// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<SettingsService>();
// AddHttpClient<T> registers PlaidService as a typed HttpClient consumer with
// proper socket pooling via IHttpClientFactory. Do NOT also call AddScoped<PlaidService>
// as that would override this registration with a plain scoped instance.
// Plaid-Version header is set centrally here so all requests use a consistent API version.
builder.Services.AddHttpClient<PlaidService>(client =>
{
    client.DefaultRequestHeaders.Add("Plaid-Version", "2020-09-14");
});
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ReportService>();

// ── HttpClient for Blazor pages calling local API endpoints ───────────────
// Derive the base address from ASPNETCORE_URLS so it works in both dev and Docker.
// In dev, launchSettings sets the URL to http://localhost:5200.
// In Docker, ASPNETCORE_URLS is set to http://+:8080 which we normalize to localhost.
builder.Services.AddScoped<HttpClient>(sp =>
{
    var urls = builder.Configuration["ASPNETCORE_URLS"]
               ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
               ?? (builder.Environment.IsDevelopment() ? "http://localhost:5200" : "http://localhost:8080");

    // Normalize "http://+:PORT" → "http://localhost:PORT"
    var firstUrl = urls.Split(';')[0]
        .Replace("http://+:", "http://localhost:")
        .Replace("https://+:", "https://localhost:")
        .TrimEnd('/');

    return new HttpClient { BaseAddress = new Uri(firstUrl + "/") };
});

var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseStaticFiles();
app.UseRouting();

// Gate the debug controller to Development environment only
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api/debug"))
        {
            context.Response.StatusCode = 404;
            return;
        }
        await next();
    });
}

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();