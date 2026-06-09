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
            // ItemId groups all accounts belonging to one Plaid Item (bank login).
            // Indexed for efficient group-delete during RemoveItem.
            e.Property(x => x.ItemId).IsRequired().HasDefaultValue("");
            e.HasIndex(x => x.ItemId);
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
            e.HasKey(x => x.Id);
            // Single row: Id = 1. Seed defaults here.
            e.HasData(new AppSetting { Id = 1, PlaidEnvironment = "sandbox" });
        });
    }
}