# Implementation Plan — Typed Settings Schema & Dynamic Year

We will restructure the settings table to have typed columns instead of a key-value structure, and transition the active year configuration from a database setting to a dynamic lookup derived from the last available transaction.

## User Review Required

> [!IMPORTANT]
> - **Database Schema Migration**: A new database migration `UpdateSettingsSchema` will be created to drop the EAV `app_settings` (Key/Value) table structure and create a typed table (`id` primary key, `plaid_environment` text column). Data in existing databases will be reset to sandbox defaults.

## Proposed Changes

---

### Backend Models & DbContext

#### [MODIFY] [AppSetting.cs](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Models/AppSetting.cs)
- Redefine `AppSetting` to:
  ```csharp
  public class AppSetting
  {
      public int Id { get; set; } = 1;
      public string PlaidEnvironment { get; set; } = "sandbox";
  }
  ```

#### [MODIFY] [AppDbContext.cs](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Data/AppDbContext.cs)
- Update `OnModelCreating` configuration for `AppSetting` to use `Id` as primary key and seed a single default row with `Id = 1` and `PlaidEnvironment = "sandbox"`.

---

### Services

#### [MODIFY] [SettingsService.cs](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Services/SettingsService.cs)
- Update configuration loading/saving to manage the single row in the structured `app_settings` table.
- Implement dynamic `GetOutputYear()` logic to find the year of the last available transaction in the database, falling back to `DateTime.UtcNow.Year` if empty.
- Keep `GetAll()` returning a dictionary containing `plaid_environment` and `output_year` (for frontends).

---

### Controllers

#### [MODIFY] [SettingsController.cs](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Controllers/SettingsController.cs)
- Remove `output_year` validation and saving logic. Only permit updates for `plaid_environment`.

---

### Frontend Components

#### [MODIFY] [Settings.razor](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Pages/Settings.razor)
- Remove the "Year" selection UI, fields, and save logic.

#### [MODIFY] [Reports.razor](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Pages/Reports.razor)
- Load the dynamic default year from `api/settings` on initialization, updating `_year` so the reports match the last active year on page load.

#### [MODIFY] [Transactions.razor](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Pages/Transactions.razor)
- Load the dynamic default year from `api/settings` on initialization, updating `_filterYear` accordingly.

---

### Database Migration

#### [NEW] [Migration Files](file:///c:/Users/Neil/Documents/Repos/Spening/Spening/Data/Migrations)
- Generate a new migration:
  ```powershell
  dotnet ef migrations add UpdateSettingsSchema
  ```

---

## Verification Plan

### Automated Tests
- Verify successful compilation with `dotnet build`.
- Generate and verify database migrations.
