# Finances.App — User guide

Welcome! Finances.App is a private finance tracker for a small salon. It runs
entirely in your browser: no account, no sign-up, no server. Everything you
enter lives in this browser profile on this device.

## First steps

1. Open the app and pick your language from the top bar (English / Polski).
2. Add your workers, services, and products from the **Manage** section.
3. Record a couple of services or product sales to see the dashboard come to
   life.
4. When you're comfortable, open the **Data & Updates** page and press
   *Export* to back up your JSON — that's your escape hatch when you switch
   browsers or devices.

## Core workflows

### Services & sales

- **Record Service** — log one service at a time (worker, client, price,
  tips, commission override, notes).
- **Product Sales** — retail checkout (unit price, quantity, optional
  client).
- **Service History** — filterable table with edit/delete; export to Excel
  or PDF.

### Clients

- **Clients list** — search by name/phone/email, export the full list, open
  a client profile.
- **Client profile** — recent service history, product sales, totals, and a
  timeline of comments. Each comment can carry an optional reminder date —
  when it comes due, the in-app bell and (if you opted in) a desktop
  notification will fire.

### Expenses & invoices

- **Expenses** — track business costs by category with date range + text
  filters. Revenue minus expenses shows up on the Dashboard and the Analytics
  page as net profit (tips are excluded from revenue).
- **Invoices** — manual entry with line items, paid/unpaid status, currency,
  and optional PDF/photo attachments stored in IndexedDB. If you attach a
  PDF, the app tries to auto-fill vendor, date, total, and currency — always
  double-check before saving.

### Recurring visits & goals

- **Recurring** — schedule repeat appointments; the bell surfaces them when
  they come due or go overdue.
- **Analytics & Dashboard** — revenue sources, busiest days, top products,
  goal progress, retention, and net profit.

## Universal search

The top-bar search bar works in three modes:

1. **Off** (default) — the bar is hidden behind your explicit choice. Turn
   it on from the gear icon inside the bar.
2. **Keyword** — fast substring match across every text field in every
   entity. Always available, nothing is downloaded.
3. **Semantic (on-device)** — opt-in. The app downloads a ~120 MB
   multilingual model (MiniLM-L12) once, caches it in your browser, and then
   searches by meaning across English and Polish. Great for finding "the
   client who complained about itchy scalp" even if you wrote it in Polish
   three months ago.

You can switch modes any time from the gear icon inside the search bar, and
you can purge the downloaded model from the Data & Updates page.

### Tips

- Search results are grouped by entity type (Clients, Comments, Expenses,
  Invoices, …). Click a result to jump straight to the edit modal.
- Keyword matches are marked with a small *keyword* chip so you know the
  hit was an exact substring rather than a semantic match.
- If the semantic model hasn't finished downloading, search falls back to
  keyword mode automatically so you never see a blank page.

## Notifications

- The bell icon in the top bar surfaces overdue recurring services, low stock
  (≤ 3 units), and due client-comment reminders.
- Browser notifications are opt-in. Accept the banner and the app will also
  pop a system notification when a comment reminder fires. You can dismiss
  the prompt permanently from the banner or re-enable it from your browser's
  site settings.

## Backups & updates

- **Data & Updates** page:
  - Export / import JSON backups.
  - Reset all data (writes a backup first).
  - Device storage diagnostics: see quota, attachment totals, and the largest
    file type.
  - *Clear runtime caches*: forget cached PDF.js, OCR, and semantic model
    weights. Your records and attachments stay put — next use of those
    features will re-download from the CDN.
  - Manual PWA update check: compares the installed build against the
    pending build and reloads once the new one is ready.

When you install a new version of the app, a toast confirms the switch-over
so you know the PWA actually updated.

## Privacy

- No data leaves your browser unless you export it manually.
- No account, no analytics, no tracking.
- Anyone with access to the same browser profile on the same device can
  still read your data — if that's a concern, export a backup and clear the
  browser profile.

## Troubleshooting

- **Search says "No matches"** even for exact phrases: open the gear icon in
  the search bar and make sure a mode other than *Off* is selected.
- **Model download stuck**: switch to *Keyword* mode, then *Clear runtime
  caches* on the Data page, then re-enable *Semantic* to start over.
- **Browser notifications silent**: system DND can override browser opt-in.
  Check your OS notification settings for this browser.
- **Lost data after switching browsers**: import the JSON backup you
  exported earlier. There is no way to recover data that was never exported.

## Keyboard shortcuts

- `Enter` in the search bar opens the first hit.
- `Esc` closes the search dropdown.
