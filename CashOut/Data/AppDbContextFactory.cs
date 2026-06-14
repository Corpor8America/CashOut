using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Load .env file automatically
        DotNetEnv.Env.Load("../.env");

        var config = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();

        var conn = config["ConnectionStrings:Default"];

        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException("ConnectionStrings:Default is required.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn)
            .Options;

        return new AppDbContext(options);
    }
}
