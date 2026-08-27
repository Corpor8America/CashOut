using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;
using Testcontainers.PostgreSql;

namespace CashOut.Tests.UiTests.Helpers;

public class CashOutAppFactory : IDisposable
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("cashout")
        .WithUsername("cashout")
        .WithPassword("testpass")
        .Build();

    private WebApplication? _app;

    public string ConnectionString => _db.GetConnectionString();
    public string BaseUrl => $"http://127.0.0.1:{_port}";
    public HttpClient Api { get; private set; } = null!;

    private int _port;

    public async Task StartAsync()
    {
        await _db.StartAsync();

        _port = GetAvailablePort();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://127.0.0.1:{_port}");

        var cashOutProjectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CashOut"));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            ContentRootPath = cashOutProjectDir
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = ConnectionString
        });

        var culture = new System.Globalization.CultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;

        builder.Services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(ConnectionString));

        var cashOutAssembly = typeof(Program).Assembly;

        builder.Services.AddRazorPages().AddApplicationPart(cashOutAssembly);
        builder.Services.AddServerSideBlazor();
        builder.Services.AddControllers().AddApplicationPart(cashOutAssembly);
        builder.Services.AddControllers();
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
        {
            o.MultipartBodyLengthLimit = 11 * 1024 * 1024;
        });

        builder.Services.AddScoped<SettingsService>();
        builder.Services.AddScoped<CsvImportService>();
        builder.Services.AddScoped<PdfImportService>();
        builder.Services.AddScoped<TransactionService>();
        builder.Services.AddScoped<ReportService>();
        builder.Services.AddScoped<CategoryService>();
        builder.Services.AddScoped<CategoryRuleService>();

        builder.Services.AddMudServices();

        builder.Services.AddScoped<HttpClient>(sp =>
        {
            return new HttpClient { BaseAddress = new Uri(BaseUrl + "/") };
        });

        _app = builder.Build();

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();
        }

        _app.UseStaticFiles();
        _app.UseRouting();
        _app.MapControllers();
        _app.MapBlazorHub();
        _app.MapFallbackToPage("/_Host");

        await _app.StartAsync();

        Api = new HttpClient { BaseAddress = new Uri(BaseUrl + "/") };
    }

    public async Task DisposeAsync()
    {
        Api?.Dispose();
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        await _db.DisposeAsync();
    }

    void IDisposable.Dispose() => DisposeAsync().GetAwaiter().GetResult();

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
