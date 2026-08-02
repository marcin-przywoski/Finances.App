# Finances.App

This branch is a standalone Blazor WebAssembly and PWA build of the app.

## What changed

- All CRUD and analytics behavior runs in the browser through a local data store.
- Data is persisted in browser `localStorage` instead of SQLite or ASP.NET controllers.
- The `Data` page lets the user export, import, or reset a local JSON backup.
- The client now includes a manifest, service worker, offline cache, and installable PWA assets.

## Projects

- `Finances.App.Client` — the Blazor WebAssembly app.
- `Finances.App.Shared` — the data models.
- `Finances.App.Client.Tests` — fast xUnit + bUnit tests for the store, analytics, and components.
- `Finances.App.PlaywrightTests` — browser end-to-end tests against real published output (tagged `Category=E2E`).

## Privacy model

- Data never leaves the browser unless the user exports it.
- There is no server-side login or shared database in this standalone build.
- Anyone with access to the same browser profile on the same machine can still read that browser storage.

## Data safety

- If saved data cannot be read (corruption, or a backup from a newer app version), the original bytes are preserved and offered for download on the `Data` page — never silently overwritten.
- Saves that fail (for example when the ~5 MB browser storage quota is full) report an actionable error instead of losing the record; the `Data` page shows current storage usage.
- Editing in two tabs at once is detected: the second tab refreshes and asks you to retry instead of silently overwriting the first tab's changes.

## Build and test

```powershell
dotnet build Finances.App.sln
dotnet test Finances.App.sln --filter "Category!=E2E"   # fast unit tests
dotnet test Finances.App.PlaywrightTests                # browser E2E (publishes the app, needs Playwright Chromium)
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
- Human-readable release notes come from `Finances.App.Client/wwwroot/pwa-release.json`. Write the title/summary/changes by hand; the deploy workflow stamps `releaseId` and `publishedUtc` from the commit automatically, so they cannot go stale.
- When a newer build becomes active, the app shows a confirmation toast so the user knows the installed PWA actually switched over.
- Export a backup before clearing browser data or moving to another device.

## Using the app

- Open the `Data` page to export a backup before switching browsers or devices.
- Import that JSON file in another browser to restore the same records locally.