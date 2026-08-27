using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Features;
using System.Globalization;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 11 * 1024 * 1024;
});

// ── Services ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<SettingsService>();

builder.Services.AddScoped<CsvImportService>();
builder.Services.AddScoped<PdfImportService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<CategoryRuleService>();

builder.Services.AddMudServices();

// ── HttpClient for Blazor pages ───────────────────────────────────────────
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

// ── Auto-migrate on startup ──────────────────────────────────────────────
{
    var maxRetries = 10;
    for (var attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < maxRetries)
        {
            var delay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
            Console.WriteLine($"Migration attempt {attempt}/{maxRetries} failed ({ex.GetType().Name}). Retrying in {delay.TotalSeconds}s...");
            Thread.Sleep(delay);
        }
    }
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

public partial class Program { }
