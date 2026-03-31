# KubiczPlace.Finances

This branch is a standalone Blazor WebAssembly and PWA build of the app.

## What changed

- The solution is trimmed to `KubiczPlace.Finances.Client` plus `KubiczPlace.Finances.Shared`.
- All CRUD and analytics behavior runs in the browser through a local data store.
- Data is persisted in browser `localStorage` instead of SQLite or ASP.NET controllers.
- The `Data` page lets the user export, import, or reset a local JSON backup.
- The client now includes a manifest, service worker, offline cache, and installable PWA assets.

## Privacy model

- Data never leaves the browser unless the user exports it.
- There is no server-side login or shared database in this standalone build.
- Anyone with access to the same browser profile on the same machine can still read that browser storage.

## Build

```powershell
dotnet build KubiczPlace.Finances.sln
```

## Run locally

```powershell
dotnet run --project .\KubiczPlace.Finances.Client\KubiczPlace.Finances.Client.csproj
```

## Publish the standalone site

```powershell
dotnet publish .\KubiczPlace.Finances.Client\KubiczPlace.Finances.Client.csproj -c Release
```

Publish output is written to:

```text
KubiczPlace.Finances.Client\bin\Release\net9.0\publish\wwwroot
```

Deploy the contents of that `wwwroot` folder to any static host.

## GitHub Pages notes

- The repository includes a GitHub Actions workflow at `.github/workflows/deploy-pages.yml` that publishes this branch to GitHub Pages.
- The workflow publishes only the standalone client project and uploads the generated `wwwroot` directory.
- The client includes a `.nojekyll` file so `_framework` assets are served correctly on Pages.
- `404.html` uses `pathSegmentsToKeep = 1`, which is correct for a repository site such as `/KubiczPlace.Finances/`.
- If you host the app at a custom domain root instead, change `pathSegmentsToKeep` to `0` in `KubiczPlace.Finances.Client/wwwroot/404.html`.

## PWA notes

- The app can be installed from supported browsers and will cache its static assets for offline use.
- The service worker only provides the full offline experience after the first successful online load.
- The `Data` page now includes a manual update workflow: check for updates, see the current offline build, and reload once when a new build is ready.
- When a newer build becomes active, the app shows a confirmation toast so the user knows the installed PWA actually switched over.
- Export a backup before clearing browser data or moving to another device.

## Using the app

- Open the `Data` page to export a backup before switching browsers or devices.
- Import that JSON file in another browser to restore the same records locally.