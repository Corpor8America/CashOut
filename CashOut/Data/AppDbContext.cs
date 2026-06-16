using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<ManualAccount> ManualAccounts => Set<ManualAccount>();
    public DbSet<RawBusiness> RawBusinesses => Set<RawBusiness>();
    public DbSet<BusinessAlias> BusinessAliases => Set<BusinessAlias>();
    public DbSet<AliasPattern> AliasPatterns => Set<AliasPattern>();
    public DbSet<RawBusinessAliasMap> RawBusinessAliasMaps => Set<RawBusinessAliasMap>();
    public DbSet<CsvMappingProfile> CsvMappingProfiles => Set<CsvMappingProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── LinkedAccount ─────────────────────────────────────────────────
        modelBuilder.Entity<LinkedAccount>(e =>
        {
            e.ToTable("linked_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AccountId).IsRequired();
            e.HasIndex(x => x.AccountId).IsUnique();
            e.Property(x => x.ItemId).IsRequired().HasDefaultValue("");
            e.HasIndex(x => x.ItemId);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        // ── ManualAccount ─────────────────────────────────────────────────
        modelBuilder.Entity<ManualAccount>(e =>
        {
            e.ToTable("manual_accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });

        // ── Transaction ───────────────────────────────────────────────────
        modelBuilder.Entity<Transaction>(e =>
        {
            e.ToTable("transactions");
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.TransactionId).ValueGeneratedNever();
            e.Property(x => x.Source).HasConversion<string>().IsRequired();
            e.Property(x => x.Credit).IsRequired(false);
            e.Property(x => x.Debit).IsRequired(false);
            e.Property(x => x.Amount).IsRequired();
            e.Property(x => x.DedupKey).IsRequired(false);
            e.Property(x => x.RawName).IsRequired().HasDefaultValue("");
            e.Property(x => x.NormalizedName).IsRequired().HasDefaultValue("");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.HasOne(x => x.Alias).WithMany().HasForeignKey(x => x.AliasId);
            e.HasOne(x => x.RawBusiness).WithMany().HasForeignKey(x => x.RawBusinessId);
        });

        // ── AppSetting ────────────────────────────────────────────────────
        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("app_settings");
            e.HasKey(x => x.Id);
            e.HasData(new AppSetting { Id = 1 });
        });

        // ── RawBusiness ───────────────────────────────────────────────────
        modelBuilder.Entity<RawBusiness>(e =>
        {
            e.ToTable("raw_businesses");
            e.HasKey(x => x.Id);
            e.Property(x => x.RawName).IsRequired();
            e.Property(x => x.RawNameNormalized).IsRequired().HasDefaultValue("");
            e.HasIndex(x => x.RawNameNormalized).IsUnique();
            e.Property(x => x.CategoryRaw).IsRequired().HasDefaultValue("");
            e.Property(x => x.IsMapped).IsRequired().HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        // ── BusinessAlias ─────────────────────────────────────────────────
        modelBuilder.Entity<BusinessAlias>(e =>
        {
            e.ToTable("business_aliases");
            e.HasKey(x => x.Id);
            e.Property(x => x.AliasName).IsRequired();
            e.HasIndex(x => x.AliasName).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.HasMany(x => x.Patterns)
             .WithOne(x => x.Alias)
             .HasForeignKey(x => x.AliasId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── AliasPattern ──────────────────────────────────────────────────
        modelBuilder.Entity<AliasPattern>(e =>
        {
            e.ToTable("alias_patterns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Pattern).IsRequired();
            e.Property(x => x.MatchType).HasConversion<string>().IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
            e.HasOne(x => x.Alias)
             .WithMany(x => x.Patterns)
             .HasForeignKey(x => x.AliasId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RawBusinessAliasMap ───────────────────────────────────────────
        modelBuilder.Entity<RawBusinessAliasMap>(e =>
        {
            e.ToTable("raw_business_alias_map");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RawBusinessId).IsUnique();
            e.HasOne(x => x.RawBusiness).WithMany().HasForeignKey(x => x.RawBusinessId);
            e.HasOne(x => x.Alias).WithMany().HasForeignKey(x => x.AliasId);
        });

        // ── CsvMappingProfile ─────────────────────────────────────────────
        modelBuilder.Entity<CsvMappingProfile>(e =>
        {
            e.ToTable("csv_mapping_profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.AccountId).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        });
    }
}
