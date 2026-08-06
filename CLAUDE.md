# Finances.App

Standalone .NET 10 Blazor WebAssembly PWA — a salon/barbershop finance tracker
(workers with commission splits, service catalog, products with stock, service
records, product sales, expenses, analytics with a linear-regression forecast).

## Hard constraints

- **Client-only by design.** All data lives in browser `localStorage`; the only
  way data leaves the browser is the user's JSON export. Never propose adding a
  server, backend, account system, or telemetry.
- **Snapshot format is a compatibility contract.** Persisted under the
  `Finances.App.snapshot` key as camelCase JSON via the source-generated
  `FinanceJsonContext`. Old backups must keep importing — the legacy-format
  fixture test in `Finances.App.Client.Tests/DataSafetyTests.cs` and the
  migration tests in `SchemaV2Tests.cs` pin this.
- **Schema versioning rule:** current schema is v2 (v2 added `expenses` and
  `settings`). Bump the version only for breaking shape changes and add a
  step in `MigrateSnapshot`; a new *optional* property whose default is filled
  in `NormalizeSnapshot` is additive and must NOT bump the version.
- User preferences (currency, language) live in the snapshot's `settings`
  object so they travel with backups; money display goes through the
  `MoneyFormat` service, never `ToString("C")` directly in pages.
- **Localization**: UI strings come from `IStringLocalizer<AppStrings>`
  (`Resources/AppStrings.resx` neutral English + `AppStrings.pl.resx`; keys
  are `Page_Element` with a `Common_` prefix). Validation messages use
  `ValidationStrings` in Shared (hand-written accessor — the resx code
  generator does not run under `dotnet build`). Every key must exist in both
  files — `LocalizationTests` pins the key sets. The language is applied at
  boot in `Program.Main`; the client ships full ICU
  (`BlazorWebAssemblyLoadAllGlobalizationData`) because Polish is outside the
  EFIGS shard. `DateKey.ToDateKey()` stays invariant — it is a storage
  contract, not a display format; do not "fix" it to the current culture.
- Related storage keys: `.quarantine` (preserved unreadable data), `.pre-reset`
  (backup written before both reset and import; surfaced as "Restore previous
  data" on the Data page), `.rev` (multi-tab write guard), `selectedWorkerId`,
  `Finances.App.theme` (light/dark/auto, owned by `js/theme.js` and applied
  pre-boot by an inline script in `index.html`), and `Finances.App.pwa.*`
  (update state, owned by `js/pwa.js`).
- Chart colors come from the CSS custom properties in `standalone-app.css`
  (read via `getComputedStyle` in `js/standalone-charts.js`) — never hardcode
  hex colors in pages or chart calls; add a token instead.
- **New installs start empty** — no placeholder workers/services. The Dashboard
  shows a first-run checklist instead. Tests that need catalog data preload the
  fixture from `Finances.App.Client.Tests/TestDoubles/TestData.cs`.

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
