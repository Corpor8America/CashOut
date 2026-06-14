---
name: cashout-ef-migrations
description: Use when working in the CashOut repo on Entity Framework Core model changes, migration files under CashOut/Data/Migrations, AppDbContext model mapping, or database schema changes. Ensures migrations include the migration .cs file, matching .Designer.cs file, updated AppDbContextModelSnapshot.cs, and verification with dotnet test/build.
---

# CashOut EF Migrations

## Workflow

When changing EF Core models or migrations in this repo:

1. Update the model and `AppDbContext` mapping together.
2. Add or update the migration `.cs` file under `CashOut/Data/Migrations`.
3. Ensure every migration has a matching `.Designer.cs` file named with the same timestamp and migration name.
4. Ensure `AppDbContextModelSnapshot.cs` reflects the post-migration model.
5. Run `dotnet test CashOut.sln` before finishing.

## Designer File Rule

Never leave a migration without its designer file.

For a migration named:

```text
YYYYMMDDHHMMSS_Name.cs
```

there must also be:

```text
YYYYMMDDHHMMSS_Name.Designer.cs
```

The designer file should:

- be in namespace `CashOut.Data.Migrations`
- include `[DbContext(typeof(AppDbContext))]`
- include `[Migration("YYYYMMDDHHMMSS_Name")]`
- declare `partial class Name`
- implement `BuildTargetModel(ModelBuilder modelBuilder)`
- represent the target model after that migration

## Verification

Before final response, check:

```powershell
Get-ChildItem CashOut\Data\Migrations -Filter *.cs
dotnet test CashOut.sln
git status --short
```

Mention any remaining warnings from existing code separately from migration failures.
