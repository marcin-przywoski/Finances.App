using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Finances.App.Shared;
using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

public enum SemanticSearchMode
{
    Disabled,
    Keyword,
    Semantic
}

public sealed record SemanticSearchHit(
    string EntityType,
    int EntityId,
    string FieldName,
    double Score,
    string Snippet,
    string Title,
    string RouteUrl,
    bool FallbackKeyword);

public sealed record EmbeddingProgress(
    string Status,
    long Loaded,
    long Total,
    double Percent,
    string? File);

/// <summary>
/// Universal on-device search across every text-shaped field on the
/// <see cref="IFinanceService"/> corpus. Supports three modes:
/// <list type="bullet">
/// <item><c>Disabled</c>: the feature is off (default until the user opts in).</item>
/// <item><c>Keyword</c>: case-insensitive substring match — always available.</item>
/// <item><c>Semantic</c>: transformers.js embeddings cached in IndexedDB.
/// The ~120 MB model is downloaded on first enable and never bundled.</item>
/// </list>
/// The service is deliberately conservative: it never triggers model download
/// without explicit consent, and callers can always fall back to keyword
/// search while the model is loading or on failure.
/// </summary>
public sealed class SemanticSearchService : IAsyncDisposable
{
    private const string ModeKey = "Finances.App.search.mode";
    private const string PromptDismissedKey = "Finances.App.search.prompt.dismissed";

    private readonly IJSRuntime _js;
    private readonly IFinanceService _finance;
    private readonly LocalizationService _loc;

    private DotNetObjectReference<SemanticSearchService>? _selfRef;
    private IJSObjectReference? _progressSubscription;
    private bool _initialized;
    private bool _modelLoading;
    private bool _modelReady;
    private int _indexedCount;
    private int _indexingTotal;
    private bool _indexingBusy;
    private CancellationTokenSource? _indexerDebounce;
    private readonly SemaphoreSlim _indexerGate = new(1, 1);
    private IProgress<EmbeddingProgress>? _activeProgress;

    public SemanticSearchService(IJSRuntime js, IFinanceService finance, LocalizationService loc)
    {
        _js = js;
        _finance = finance;
        _loc = loc;
        _finance.OnChange += HandleFinanceChanged;
    }

    public SemanticSearchMode Mode { get; private set; } = SemanticSearchMode.Disabled;
    public bool ModelReady => _modelReady;
    public bool ModelLoading => _modelLoading;
    public int IndexedCount => _indexedCount;
    public int IndexingTotal => _indexingTotal;
    public bool IsIndexing => _indexingBusy;

    public event Action? OnChange;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", ModeKey);
            Mode = ParseMode(stored);
        }
        catch
        {
            Mode = SemanticSearchMode.Disabled;
        }

        _initialized = true;

        if (Mode == SemanticSearchMode.Semantic)
        {
            // Fire-and-forget: don't block startup on model load.
            _ = Task.Run(async () =>
            {
                try { await EnsureModelLoadedAsync(null); }
                catch { /* swallowed — UI surfaces errors via OnChange */ }
            });
        }
    }

    public async Task<bool> IsPromptDismissedAsync()
    {
        try
        {
            var value = await _js.InvokeAsync<string?>("localStorage.getItem", PromptDismissedKey);
            return value == "true";
        }
        catch
        {
            return false;
        }
    }

    public async Task MarkPromptDismissedAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", PromptDismissedKey, "true");
        }
        catch { /* best-effort */ }
        NotifyChanged();
    }

    public async Task SetModeAsync(SemanticSearchMode mode, IProgress<EmbeddingProgress>? progress = null)
    {
        Mode = mode;
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", ModeKey, mode.ToString().ToLowerInvariant());
        }
        catch { /* best-effort */ }

        if (mode == SemanticSearchMode.Semantic)
        {
            await EnsureModelLoadedAsync(progress);
            await ReindexAllAsync();
        }
        else if (mode == SemanticSearchMode.Disabled)
        {
            await ClearEmbeddingsAsync();
        }

        NotifyChanged();
    }

    public async Task EnsureModelLoadedAsync(IProgress<EmbeddingProgress>? progress)
    {
        if (_modelReady) return;
        if (_modelLoading) return;

        _modelLoading = true;
        _activeProgress = progress;
        NotifyChanged();

        try
        {
            _selfRef ??= DotNetObjectReference.Create(this);
            _progressSubscription = await _js.InvokeAsync<IJSObjectReference>("financeEmbeddings.registerDotNetProgress", _selfRef);
            await _js.InvokeAsync<object?>("financeEmbeddings.ensureModel");
            _modelReady = true;
        }
        catch
        {
            _modelReady = false;
            throw;
        }
        finally
        {
            _modelLoading = false;
            _activeProgress = null;
            NotifyChanged();
        }
    }

    [JSInvokable]
    public void OnProgress(string? status, long loaded, long total, double percent, string? file)
    {
        _activeProgress?.Report(new EmbeddingProgress(
            status ?? string.Empty,
            loaded,
            total,
            percent > 1 ? percent : percent * 100,
            file));
    }

    public async Task<IReadOnlyList<SemanticSearchHit>> QueryAsync(string query, int topK = 25)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SemanticSearchHit>();
        query = query.Trim();
        if (query.Length < 2) return Array.Empty<SemanticSearchHit>();

        var corpus = await BuildCorpusAsync();

        if (Mode == SemanticSearchMode.Semantic && _modelReady)
        {
            try
            {
                return await SemanticQueryAsync(query, corpus, topK);
            }
            catch
            {
                // transformers.js may throw under storage pressure or offline;
                // fall through to keyword search so users still get results.
            }
        }

        return KeywordQuery(query, corpus, topK);
    }

    private async Task<IReadOnlyList<SemanticSearchHit>> SemanticQueryAsync(string query, IReadOnlyList<CorpusField> corpus, int topK)
    {
        var vector = await _js.InvokeAsync<double[]?>("financeEmbeddings.embed", query);
        if (vector is null || vector.Length == 0)
        {
            return KeywordQuery(query, corpus, topK);
        }

        var floats = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++) floats[i] = (float)vector[i];

        var hits = await _js.InvokeAsync<StoreHit[]?>("financeStore.queryTopK", floats, topK, 0.35);
        if (hits is null || hits.Length == 0)
        {
            return KeywordQuery(query, corpus, topK);
        }

        var lookup = corpus.ToLookup(f => $"{f.EntityType}:{f.EntityId}:{f.FieldName}");
        var results = new List<SemanticSearchHit>();

        foreach (var hit in hits)
        {
            if (string.IsNullOrEmpty(hit.Key)) continue;
            var field = lookup[hit.Key].FirstOrDefault();
            if (field is null) continue;

            results.Add(new SemanticSearchHit(
                field.EntityType,
                field.EntityId,
                field.FieldName,
                hit.Score,
                BuildSnippet(field.Text, query),
                field.Title,
                field.RouteUrl,
                FallbackKeyword: false));
        }

        return results.Count > 0 ? results : KeywordQuery(query, corpus, topK);
    }

    private IReadOnlyList<SemanticSearchHit> KeywordQuery(string query, IReadOnlyList<CorpusField> corpus, int topK)
    {
        var needle = query.Trim();
        var lowerNeedle = needle.ToLowerInvariant();
        var hits = new List<SemanticSearchHit>();

        foreach (var field in corpus)
        {
            if (string.IsNullOrWhiteSpace(field.Text)) continue;
            var lowerText = field.Text.ToLowerInvariant();
            var matchIndex = lowerText.IndexOf(lowerNeedle, StringComparison.Ordinal);
            if (matchIndex < 0) continue;

            // Simple score: earlier matches in shorter fields score higher.
            var lengthPenalty = 1.0 / Math.Max(1, field.Text.Length / 40.0);
            var positionBonus = 1.0 - Math.Min(0.8, matchIndex / 100.0);
            var score = Math.Clamp(0.3 + 0.35 * lengthPenalty + 0.35 * positionBonus, 0.0, 1.0);

            hits.Add(new SemanticSearchHit(
                field.EntityType,
                field.EntityId,
                field.FieldName,
                score,
                BuildSnippet(field.Text, needle, matchIndex),
                field.Title,
                field.RouteUrl,
                FallbackKeyword: true));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .Take(topK)
            .ToList();
    }

    public async Task ReindexAllAsync(IProgress<(int Done, int Total)>? progress = null)
    {
        if (Mode != SemanticSearchMode.Semantic || !_modelReady) return;
        await _indexerGate.WaitAsync();

        try
        {
            _indexingBusy = true;
            _indexedCount = 0;
            NotifyChanged();

            var corpus = await BuildCorpusAsync();
            _indexingTotal = corpus.Count;
            NotifyChanged();

            foreach (var field in corpus)
            {
                var key = $"{field.EntityType}:{field.EntityId}:{field.FieldName}";
                var hash = HashText(field.Text);
                try
                {
                    var existing = await _js.InvokeAsync<string?>("financeStore.getEmbeddingHash", key);
                    if (string.Equals(existing, hash, StringComparison.Ordinal))
                    {
                        _indexedCount++;
                        progress?.Report((_indexedCount, _indexingTotal));
                        continue;
                    }

                    var vector = await _js.InvokeAsync<double[]?>("financeEmbeddings.embed", field.Text);
                    if (vector is null || vector.Length == 0)
                    {
                        _indexedCount++;
                        progress?.Report((_indexedCount, _indexingTotal));
                        continue;
                    }

                    var floats = new float[vector.Length];
                    for (var i = 0; i < vector.Length; i++) floats[i] = (float)vector[i];
                    await _js.InvokeVoidAsync("financeStore.putEmbedding", key, floats, hash);
                }
                catch
                {
                    // Individual failures should never stop the whole reindex.
                }

                _indexedCount++;
                progress?.Report((_indexedCount, _indexingTotal));
                if (_indexedCount % 10 == 0) NotifyChanged();
            }
        }
        finally
        {
            _indexingBusy = false;
            _indexerGate.Release();
            NotifyChanged();
        }
    }

    public async Task ClearEmbeddingsAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("financeStore.clearEmbeddings");
        }
        catch { /* best-effort */ }
        _indexedCount = 0;
        _indexingTotal = 0;
        NotifyChanged();
    }

    private void HandleFinanceChanged()
    {
        if (Mode != SemanticSearchMode.Semantic || !_modelReady) return;

        _indexerDebounce?.Cancel();
        _indexerDebounce = new CancellationTokenSource();
        var token = _indexerDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(750, token);
                if (token.IsCancellationRequested) return;
                await ReindexAllAsync();
            }
            catch (TaskCanceledException) { /* expected */ }
            catch { /* best-effort */ }
        }, token);
    }

    private static string HashText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string BuildSnippet(string text, string query, int? start = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (text.Length <= 160) return text;

        var index = start ?? text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = 0;

        var windowStart = Math.Max(0, index - 30);
        var windowEnd = Math.Min(text.Length, windowStart + 160);
        windowStart = Math.Max(0, windowEnd - 160);

        var snippet = text.Substring(windowStart, windowEnd - windowStart);
        if (windowStart > 0) snippet = "…" + snippet;
        if (windowEnd < text.Length) snippet += "…";
        return snippet;
    }

    private async Task<IReadOnlyList<CorpusField>> BuildCorpusAsync()
    {
        var rows = new List<CorpusField>();

        var clients = await _finance.GetClientsAsync();
        foreach (var c in clients)
        {
            var route = $"/clients/{c.Id}";
            AddField(rows, "client", c.Id, "Name", c.Name, c.Name, route);
            AddField(rows, "client", c.Id, "Phone", c.Phone, c.Name, route);
            AddField(rows, "client", c.Id, "Email", c.Email, c.Name, route);
            AddField(rows, "client", c.Id, "Notes", c.Notes, c.Name, route);

            var comments = await _finance.GetClientCommentsAsync(c.Id);
            foreach (var cm in comments)
            {
                AddField(rows, "comment", cm.Id, "Body", cm.Body,
                    $"{c.Name} · {cm.CreatedUtc:yyyy-MM-dd}", route);
            }
        }

        var workers = await _finance.GetWorkersAsync();
        foreach (var w in workers)
        {
            AddField(rows, "worker", w.Id, "Name", w.Name, w.Name, "/workers");
        }

        var services = await _finance.GetServicesAsync();
        foreach (var s in services)
        {
            AddField(rows, "service", s.Id, "Name", s.Name, s.Name, "/services");
        }

        var products = await _finance.GetProductsAsync();
        foreach (var p in products)
        {
            AddField(rows, "product", p.Id, "Name", p.Name, p.Name, "/products");
            AddField(rows, "product", p.Id, "Category", p.Category, p.Name, "/products");
        }

        var records = await _finance.GetServiceRecordsAsync();
        foreach (var r in records)
        {
            var title = r.Service?.Name ?? r.ClientName ?? r.DatePerformed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var route = $"/records/{r.Id}";
            AddField(rows, "record", r.Id, "Notes", r.Notes, title, route);
            AddField(rows, "record", r.Id, "ClientName", r.ClientName, title, route);
        }

        var sales = await _finance.GetProductSalesAsync();
        foreach (var s in sales)
        {
            var title = s.Product?.Name ?? s.ClientName ?? s.DateSold.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var route = $"/sales/{s.Id}";
            AddField(rows, "sale", s.Id, "Notes", s.Notes, title, route);
            AddField(rows, "sale", s.Id, "ClientName", s.ClientName, title, route);
        }

        var recurring = await _finance.GetRecurringServicesAsync();
        foreach (var rc in recurring)
        {
            var title = rc.Service?.Name ?? rc.NextOccurrence.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            AddField(rows, "recurring", rc.Id, "Notes", rc.Notes, title, "/recurring");
        }

        var goals = await _finance.GetGoalsAsync();
        foreach (var g in goals)
        {
            var title = g.Label ?? g.Type.ToString();
            AddField(rows, "goal", g.Id, "Label", g.Label, title, "/analytics");
        }

        var expenses = await _finance.GetExpensesAsync();
        foreach (var e in expenses)
        {
            var title = e.Vendor ?? e.Category.ToString();
            var route = $"/expenses/{e.Id}";
            AddField(rows, "expense", e.Id, "Vendor", e.Vendor, title, route);
            AddField(rows, "expense", e.Id, "Notes", e.Notes, title, route);
            var categoryLabel = _loc.T($"expenseCategory{e.Category}");
            if (!string.Equals(categoryLabel, $"expenseCategory{e.Category}", StringComparison.Ordinal))
            {
                AddField(rows, "expense", e.Id, "Category", categoryLabel, title, route);
            }
        }

        var invoices = await _finance.GetInvoicesAsync();
        foreach (var inv in invoices)
        {
            var title = inv.Vendor ?? inv.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var route = $"/invoices/{inv.Id}";
            AddField(rows, "invoice", inv.Id, "Vendor", inv.Vendor, title, route);
            AddField(rows, "invoice", inv.Id, "Notes", inv.Notes, title, route);

            var lines = await _finance.GetInvoiceLineItemsAsync(inv.Id);
            if (lines.Count > 0)
            {
                var joined = string.Join(" · ", lines
                    .Where(l => !string.IsNullOrWhiteSpace(l.Description))
                    .Select(l => l.Description!));
                if (!string.IsNullOrWhiteSpace(joined))
                {
                    AddField(rows, "invoice", inv.Id, "LineItems", joined, title, route);
                }
            }
        }

        return rows;
    }

    private static void AddField(ICollection<CorpusField> rows, string entityType, int entityId, string fieldName, string? text, string title, string route)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        rows.Add(new CorpusField(entityType, entityId, fieldName, text.Trim(), title, route));
    }

    private static SemanticSearchMode ParseMode(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "semantic" => SemanticSearchMode.Semantic,
            "keyword" => SemanticSearchMode.Keyword,
            _ => SemanticSearchMode.Disabled
        };
    }

    private void NotifyChanged()
    {
        OnChange?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _finance.OnChange -= HandleFinanceChanged;
        _indexerDebounce?.Cancel();
        _indexerGate.Dispose();
        if (_progressSubscription is not null)
        {
            try { await _progressSubscription.InvokeVoidAsync("dispose"); }
            catch { /* ignored */ }
            try { await _progressSubscription.DisposeAsync(); }
            catch { /* ignored */ }
        }
        _selfRef?.Dispose();
    }

    private sealed record CorpusField(
        string EntityType,
        int EntityId,
        string FieldName,
        string Text,
        string Title,
        string RouteUrl);

    private sealed class StoreHit
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("score")] public double Score { get; set; }
    }
}
