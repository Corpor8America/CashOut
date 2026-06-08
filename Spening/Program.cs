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
builder.Services.AddScoped(sp =>
{
    var nav = sp.GetRequiredService<NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
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
