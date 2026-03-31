# KubiczPlace.Finances

This branch contains a standalone Blazor WebAssembly version of the app.

## What changed

- The standalone app entry point is `KubiczPlace.Finances.Client`.
- All CRUD and analytics data is handled inside the browser.
- Data is persisted in browser `localStorage` instead of SQLite or ASP.NET controllers.
- A new `Data` page lets the user export, import, or reset their local backup.

## Privacy model

- Data never leaves the browser unless the user exports it.
- There is no server-side login or shared database in this standalone build.
- Anyone with access to the same browser profile on the same machine can still read that browser storage.

## Build

```powershell
dotnet build KubiczPlace.Finances.sln
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

- The standalone client includes a `404.html` fallback for GitHub Pages-style SPA routing.
- `404.html` currently uses `pathSegmentsToKeep = 1`, which is correct for a repository site such as `/KubiczPlace.Finances/`.
- If you host the app at a custom domain root instead, change `pathSegmentsToKeep` to `0` in `KubiczPlace.Finances.Client/wwwroot/404.html`.

## Using the app

- Open the `Data` page to export a backup before switching browsers or devices.
- Import that JSON file in another browser to restore the same records locally.