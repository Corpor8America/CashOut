using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Force US culture for currency formatting
var culture = new CultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

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

// Enable IFormFile support for CSV upload endpoints
builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 11 * 1024 * 1024; // 11 MB (slightly above 10 MB client limit)
});

// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<SettingsService>();

// AddHttpClient<T> registers PlaidService as a typed HttpClient consumer with
// proper socket pooling via IHttpClientFactory. Do NOT also call AddScoped<PlaidService>.
builder.Services.AddHttpClient<PlaidService>(client =>
{
    client.DefaultRequestHeaders.Add("Plaid-Version", "2020-09-14");
});

builder.Services.AddScoped<MerchantNormalizationService>();
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddMudServices();

// ── HttpClient for Blazor pages calling local API endpoints ───────────────
builder.Services.AddScoped<HttpClient>(sp =>
{
    var urls = builder.Configuration["ASPNETCORE_URLS"]
               ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
               ?? (builder.Environment.IsDevelopment() ? "http://localhost:5200" : "http://localhost:8080");

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