# Step 06 — UI Foundation

**Goal:** Update MainLayout nav (remove Merchants, Executive Summary, Income links; add Inflow vs Outflow). Keep ReportShell as-is. Update `_Imports.razor` if needed.

**Prerequisites:** Steps 01–05 complete.

---

## 6.1 MainLayout.razor (updated nav)

**File:** `CashOut/Shared/MainLayout.razor`

```razor
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using MudBlazor
@inherits LayoutComponentBase

<MudThemeProvider Theme="_theme" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start"
                       OnClick="@((e) => DrawerToggle())" />
        <MudSpacer />
        <MudIconButton Icon="@Icons.Material.Filled.Code" Color="Color.Inherit"
                       Href="https://github.com/corpor8america/cashout" Target="_blank" />
    </MudAppBar>
    <MudDrawer @bind-Open="_drawerOpen" Elevation="2" Variant="DrawerVariant.Mini" MiniWidth="56px">
        <MudDrawerHeader Style="padding: 0 4px;">
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2" Style="padding-left: 8px;">
                <img src="/favicon-32x32.png" alt="CashOut" />
                <MudText Typo="Typo.h6" style="padding-left:5px;">CashOut</MudText>
            </MudStack>
        </MudDrawerHeader>
        <MudNavMenu>
            @if (_drawerOpen)
            {
                <MudText Typo="Typo.subtitle2" Color="Color.Primary" Class="ml-4 mt-2 mb-1">Accounts</MudText>
            }
            <MudNavLink Href="/accounts" Icon="@Icons.Material.Filled.Link">Linked Accounts</MudNavLink>
            <MudNavLink Href="/manual-accounts" Icon="@Icons.Material.Filled.AccountBalance">Manual Accounts</MudNavLink>

            @if (_drawerOpen)
            {
                <MudText Typo="Typo.subtitle2" Color="Color.Primary" Class="ml-4 mt-4 mb-1">Data</MudText>
            }
            <MudNavLink Href="/transactions" Icon="@Icons.Material.Filled.List">Transactions</MudNavLink>

            @if (_drawerOpen)
            {
                <MudText Typo="Typo.subtitle2" Color="Color.Primary" Class="ml-4 mt-4 mb-1">Reports</MudText>
            }
            <MudNavLink Href="/reports/cashflow"
                        Icon="@Icons.Material.Filled.SwapVert">
                Inflow vs Outflow
            </MudNavLink>
            <MudNavLink Href="/reports/category"
                        Icon="@Icons.Material.Filled.Category">
                Spending by Category
            </MudNavLink>

            @if (_drawerOpen)
            {
                <MudText Typo="Typo.subtitle2" Color="Color.Primary" Class="ml-4 mt-4 mb-1">System</MudText>
            }
            <MudNavLink Href="/settings" Icon="@Icons.Material.Filled.Settings">Settings</MudNavLink>
        </MudNavMenu>
    </MudDrawer>
    <MudMainContent Class="pa-8 pt-18">
        <ErrorBoundary>
            <ChildContent>
                @Body
            </ChildContent>
            <ErrorContent Context="ex">
                <MudAlert Severity="Severity.Error" Variant="Variant.Filled" Class="my-2">
                    <strong>Something went wrong.</strong>
                    <div style="margin-top:0.4rem;font-size:0.85em;opacity:0.8">@ex.Message</div>
                </MudAlert>
            </ErrorContent>
        </ErrorBoundary>
    </MudMainContent>
</MudLayout>

@code {
    bool _drawerOpen = true;

    void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }

    private MudTheme _theme = new MudTheme()
    {
        PaletteLight = new PaletteLight()
        {
            Primary = "#2E7D32",
            PrimaryDarken = "#1B5E20",
            PrimaryLighten = "#4CAF50",
            PrimaryContrastText = "#FFFFFF",
            AppbarBackground = "#2E7D32",
            AppbarText = "#FFFFFF",
        }
    };
}
```

**Changes from v1:**
- Removed: `Executive Summary` (`/reports`) nav link
- Removed: `By Merchant` (`/reports/merchant`) nav link
- Removed: `Income` (`/reports/income`) nav link
- Removed: `Merchants & Aliases` (`/merchants`) nav link and entire "Merchants" section
- Renamed: `Cash Flow` → `Inflow vs Outflow` (same href `/reports/cashflow`)

## 6.2 ReportShell.razor (unchanged)

**File:** `CashOut/Shared/ReportShell.razor`

Keep exactly as-is from the existing codebase. No changes needed — it's already generic and doesn't reference any normalization concepts.

## 6.3 _Imports.razor (unchanged)

**File:** `CashOut/_Imports.razor`

Keep as-is. Should already include:
```razor
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using MudBlazor
@using CashOut
@using CashOut.Models
@using CashOut.Shared
```

## 6.4 DateHelper.cs (unchanged)

**File:** `CashOut/Helpers/DateHelper.cs`

Keep as-is:
```csharp
namespace CashOut.Helpers;

public static class DateHelper
{
    public static string MonthName(int month) =>
        new DateOnly(2000, month, 1).ToString("MMMM");
}
```

## 6.5 Verify build

```bash
dotnet build CashOut/CashOut.csproj
```

---

## Verification

1. Nav has exactly 5 links: Linked Accounts, Manual Accounts, Transactions, Inflow vs Outflow, Spending by Category, Settings
2. No `/reports` (Executive Summary), `/reports/merchant`, `/reports/income`, `/merchants` nav links
3. `dotnet build` succeeds
