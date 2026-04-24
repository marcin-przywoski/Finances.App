# Finances.App — Architecture

> Blazor WebAssembly standalone PWA. No backend, no server-side storage, no
> account. Everything lives in the user's browser.

## Solution layout

- `Finances.App.Client` — Blazor WebAssembly front-end (pages, services,
  static assets, PWA shell).
- `Finances.App.Shared` — POCO domain entities shared between the client and
  the test project.
- `Finances.App.Tests` — xUnit regression suite covering the local store,
  invoice parser, migration, and net-profit math.

The solution file is `Finances.App.sln`. CI runs on every PR and build
warnings are treated as errors (`-warnaserror`).

## Data layers

| Purpose | Storage | Notes |
|--|--|--|
| Primary snapshot | `localStorage` (`Finances.App.snapshot`) | Single JSON blob with every entity. Versioned via `schemaVersion`. |
| Schema backups  | `localStorage` (`Finances.App.backup.v{n}`) | Written before each migration step. |
| Attachments     | IndexedDB (`Finances.App/attachments`) | Binary blobs keyed by opaque id. |
| Embedding cache | IndexedDB (`Finances.App/embeddings`) | 384-dim float vectors + SHA-256 text hash. |
| Model weights   | IndexedDB (`transformers-cache`, managed by transformers.js) | Cached quantized MiniLM-L12 weights (~120 MB). |
| PWA state       | `localStorage` (`Finances.App.pwa.*`) | Installed build, pending update metadata, last-applied timestamp. |
| User prefs      | `localStorage` (`Finances.App.theme`, `Finances.App.locale`, notification opt-in, search mode, prompt-dismiss flags) | One key per preference. |

## Runtime dependencies (CDN, lazy-loaded)

| Library | Purpose | Runtime cache |
|--|--|--|
| PDF.js  | Extract text from uploaded supplier PDFs for invoice auto-fill. | `pdfjs-v1` |
| Tesseract.js (reserved) | Image-OCR slot for future image-receipt support. Not yet wired. | `tesseract-v1` |
| `@xenova/transformers` | On-device embeddings for universal semantic search. | `transformers-v1` |
| Hugging Face model CDN | MiniLM weights (paraphrase-multilingual-MiniLM-L12-v2, int8 quantized). | `transformers-v1` |

The service worker (`service-worker.published.js`) keeps these runtime caches
across build activations — new client versions do not force users to
re-download hundreds of megabytes. Users can purge them explicitly from the
Data page ("Clear runtime caches").

## Services (`Finances.App.Client/Services`)

- `LocalFinanceStore` — implements `IFinanceService`. Owns the JSON snapshot,
  handles CRUD, migrations, and raises `OnChange` for UI subscribers. Never
  talks to the network.
- `AttachmentStore` — thin wrapper over `wwwroot/js/store.js` for IndexedDB
  attachment blobs.
- `InvoiceParserService` — pure C# parser for invoice text. PDF text
  extraction happens in `wwwroot/js/pdf-extract.js`; pure parsing is
  unit-tested in `Finances.App.Tests`.
- `SemanticSearchService` — universal search. Walks the corpus on demand,
  deduplicates with SHA-256 text hashes, delegates to
  `wwwroot/js/embeddings.js` and `wwwroot/js/store.js` for vector
  ingestion/query. Supports three modes: `Disabled`, `Keyword` (always on,
  substring match), `Semantic` (opt-in, 120 MB model).
- `BrowserNotificationService` — wraps the browser `Notification` API.
- `NotificationService` — in-memory bell feed, derived from `IFinanceService`
  state on every snapshot change.
- `PwaUpdateService` — bridges to `wwwroot/js/pwa.js` for install,
  update-check, skip-waiting, and runtime-cache clearing flows.
- `ExportService`, `ThemeService`, `ToastService`, `WorkerContextService`,
  `LocalizationService` — utility services.

## Localization

- Two bundles, `wwwroot/locales/en-US.json` and `wwwroot/locales/pl-PL.json`.
- Parity is enforced in CI by `scripts/check-i18n-parity.py`.
- All UI strings flow through `LocalizationService`, injected as `L` in
  `_Imports.razor`.

## PWA shape

- `index.html` bootstraps Blazor WebAssembly plus a handful of lazy helper
  scripts (`store.js`, `pdf-extract.js`, `embeddings.js`, `pwa.js`).
- `manifest.webmanifest` declares the standalone-install shell.
- `service-worker.js` is a dev stub; `service-worker.published.js` is
  published builds' offline cache + runtime-cache router.
- Release metadata comes from `pwa-release.json` (human-readable "what
  changed" surfaced on the Data page).

## Deployment

- `ci.yml` runs on every push/PR: restore → i18n parity → build with
  `-warnaserror` → tests.
- `deploy-pages.yml` runs on tags matching `v*` (and on manual dispatch). It
  repeats the CI checks before publishing the `wwwroot` artifact to GitHub
  Pages. The dedicated `deploy` job uses the `github-pages` environment so
  Pages deployments show up with the correct URL.

## Extension points

- Adding a new entity type requires: a POCO in `Finances.App.Shared`, CRUD
  methods on `IFinanceService`, a migration step in `LocalFinanceStore`,
  localized strings in both bundles, and — to make it searchable — new
  `BuildCorpusAsync` rows and `searchGroup*` / `searchField*` keys in
  `SemanticSearchService` and the locale files.
- To swap the embedding model, update `MODEL_ID` in
  `wwwroot/js/embeddings.js` and bump `transformers-v1` to a new runtime
  cache name in the service worker so old weights are evicted.
