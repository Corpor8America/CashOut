using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LinkedAccount>(e =>
        {
            e.ToTable("linked_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AccountId).IsRequired();
            e.HasIndex(x => x.AccountId).IsUnique();
            e.Property(x => x.CreatedAt)
             .HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.TransactionId).ValueGeneratedNever();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("app_settings");
            e.HasKey(x => x.Key);

            // Seed default settings
            e.HasData(
                new AppSetting { Key = "plaid_environment", Value = "sandbox" },
                new AppSetting { Key = "output_year", Value = DateTime.UtcNow.Year.ToString() }
            );
        });
    }
}
