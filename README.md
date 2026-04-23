# Finances.App

Standalone Blazor WebAssembly PWA for tracking a small salon's finances —
services, product sales, recurring visits, expenses, invoices, and clients with
reminders. Runs entirely in the browser with localStorage and IndexedDB; no
backend, no account, no tracking.

## v1 feature set

- **Services & products** — CRUD for workers, services, products, sales, and
  recurring visits.
- **Clients** — dedicated client profile page with recent services, product
  sales, and a comment timeline with optional reminders.
- **Expenses** — categorized business costs with filters, Excel/PDF export, and
  net profit calculation across `Analytics` and `Dashboard`.
- **Invoices** — manual invoice entry with line items, PLN/foreign currency,
  paid/unpaid status, PDF/photo attachments, and an optional expense link.
- **Reminders & notifications** — in-app notification bell + optional browser
  notifications for due recurring visits, low stock, and comment reminders.
- **Data control** — full JSON export/import, reset, device storage
  diagnostics, and versioned backups during schema migrations.
- **Localization** — English (`en-US`) and Polish (`pl-PL`) kept in parity by CI.

## What changed

- The solution is trimmed to `Finances.App.Client`, `Finances.App.Shared`, and
  `Finances.App.Tests`.
- All CRUD and analytics behavior runs in the browser through a local data
  store.
- Data is persisted in browser `localStorage` (primary snapshot) and
  `IndexedDB` (binary attachments and embedding slots).
- The `Data` page lets the user export, import, or reset a local JSON backup
  and inspect browser storage usage.
- The client now includes a manifest, service worker, offline cache, and
  installable PWA assets.

## Privacy model

- Data never leaves the browser unless the user exports it.
- There is no server-side login or shared database in this standalone build.
- Anyone with access to the same browser profile on the same machine can still read that browser storage.

## Build

```powershell
dotnet build Finances.App.sln
```

## Run locally

```powershell
dotnet run --project .\Finances.App.Client\Finances.App.Client.csproj
```

## Publish the standalone site

```powershell
dotnet publish .\Finances.App.Client\Finances.App.Client.csproj -c Release
```

Publish output is written to:

```text
Finances.App.Client\bin\Release\net9.0\publish\wwwroot
```

Deploy the contents of that `wwwroot` folder to any static host.

## GitHub Pages notes

- The repository includes a GitHub Actions workflow at `.github/workflows/deploy-pages.yml` that publishes this branch to GitHub Pages.
- The workflow publishes only the standalone client project and uploads the generated `wwwroot` directory.
- The client includes a `.nojekyll` file so `_framework` assets are served correctly on Pages.
- `404.html` uses `pathSegmentsToKeep = 1`, which is correct for a repository site such as `/Finances.App/`.
- If you host the app at a custom domain root instead, change `pathSegmentsToKeep` to `0` in `Finances.App.Client/wwwroot/404.html`.

## PWA notes

- The app can be installed from supported browsers and will cache its static assets for offline use.
- The service worker only provides the full offline experience after the first successful online load.
- The `Data` page now includes a manual update workflow: check for updates, compare the installed build with the pending build, and reload once when a new build is ready.
- Human-readable release notes come from `Finances.App.Client/wwwroot/pwa-release.json`; update that file whenever you publish a new build if you want the PWA to show "What changed" details.
- When a newer build becomes active, the app shows a confirmation toast so the user knows the installed PWA actually switched over.
- Export a backup before clearing browser data or moving to another device.

## Using the app

- Open the `Data` page to export a backup before switching browsers or devices.
- Import that JSON file in another browser to restore the same records locally.
- Enable browser notifications when prompted to receive desktop alerts for due
  client reminders.
- Upload PDFs or photos on the `Invoices` page — they live in IndexedDB on the
  same device.

## Tests & CI

Regression coverage lives in `Finances.App.Tests` (xUnit). It exercises
`LocalFinanceStore` against an in-memory `IJSRuntime` stand-in to lock in:

- Expense round-trip (add → list → update → reload → delete)
- Client comment reminders (surfacing when due and clearing once handled)
- Schema migration from v4 → v5 (backup written, new collections initialized)
- Net profit math (revenue minus expenses, tips excluded)

```powershell
dotnet test Finances.App.sln
```

The GitHub Actions workflow at `.github/workflows/ci.yml` runs the test suite
plus an i18n parity check (`scripts/check-i18n-parity.py`) on every push and
pull request.