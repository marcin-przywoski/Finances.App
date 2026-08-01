# Finances.App

Standalone .NET 9 Blazor WebAssembly PWA — a salon/barbershop finance tracker
(workers with commission splits, service catalog, products with stock, service
records, product sales, analytics with a linear-regression forecast).

## Hard constraints

- **Client-only by design.** All data lives in browser `localStorage`; the only
  way data leaves the browser is the user's JSON export. Never propose adding a
  server, backend, account system, or telemetry.
- **Snapshot format is a compatibility contract.** Persisted under the
  `Finances.App.snapshot` key as camelCase JSON via the source-generated
  `FinanceJsonContext`. Old backups must keep importing — the legacy-format
  fixture test in `Finances.App.Client.Tests/DataSafetyTests.cs` pins this.
- Related storage keys: `.quarantine` (preserved unreadable data), `.pre-reset`
  (pre-reset backup), `.rev` (multi-tab write guard), `selectedWorkerId`, and
  `Finances.App.pwa.*` (update state, owned by `js/pwa.js`).

## Architecture

- `LocalFinanceStore` (partial: CRUD/lifecycle + `.Analytics.cs`) implements
  `IFinanceStore` and `IAnalyticsService`; pages inject the interfaces. There is
  deliberately no HTTP layer.
- Browser storage is reached only through `IKeyValueStorage`
  (`Services/Storage/`), which is what makes the store unit-testable.
- Money is `decimal`; `ServiceRecord.WorkerShare` is rounded to cents and
  `SalonShare` is the exact remainder. Date keys use `DateKey.ToDateKey()`
  (invariant `yyyy-MM-dd`) — never `ToString("yyyy-MM-dd")` with the current
  culture.

## Branches and deployment

- `wasm-standalone` is the deployed branch: `deploy-pages.yml` gates on unit
  tests, stamps `pwa-release.json` with the commit id, publishes the client,
  and deploys to GitHub Pages.
- `ci.yml` builds the solution and runs unit tests on every push to
  `wasm-standalone`, `main`, and `claude/**`, plus all pull requests; the E2E
  job runs on pull requests and the deployed branches.

## Tests

- `Finances.App.Client.Tests` — xUnit + bUnit against the real store on
  in-memory storage. Fast; run with
  `dotnet test --filter "Category!=E2E"`.
- `Finances.App.PlaywrightTests` — `[Trait("Category","E2E")]`; a collection
  fixture publishes the app (Release, trimmed) once per run, so these also
  guard the serialization contract under trimming. Chromium only; on machines
  with a pre-provisioned browser set `PLAYWRIGHT_CHROMIUM_EXECUTABLE` or rely
  on the `/opt/pw-browsers/chromium` fallback.
- Warnings are errors (`Directory.Build.props`); test projects suppress
  CA1707/CA1711 (xUnit naming) in `Directory.Build.targets`.
