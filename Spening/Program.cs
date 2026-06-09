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
builder.Services.AddHttpClient<PlaidService>();
builder.Services.AddScoped<PlaidService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ReportService>();

// ── HttpClient for Blazor pages calling local API endpoints ───────────────
// NavigationManager.BaseUri is unreliable during SSR (can be empty).
// Instead, derive the base address from the app's configured URLs at startup.
builder.Services.AddScoped<HttpClient>(sp =>
{
    // ASPNETCORE_URLS or the Kestrel config tells us where we're listening.
    // Fall back to the standard Docker port if nothing is set.
    var urls = builder.Configuration["ASPNETCORE_URLS"]
               ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
               ?? "http://localhost:8080";

    // Take the first URL (handles "http://+:8080" → "http://localhost:8080")
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
app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();