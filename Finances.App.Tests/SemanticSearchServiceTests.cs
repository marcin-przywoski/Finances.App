using Finances.App.Client.Services;
using Finances.App.Shared;
using Xunit;

namespace Finances.App.Tests;

/// <summary>
/// Keyword-mode tests for <see cref="SemanticSearchService"/>. Semantic mode
/// depends on transformers.js running in a real browser, so we only cover the
/// fallback path that never touches JS interop beyond localStorage.
/// </summary>
public class SemanticSearchServiceTests
{
    private static HttpClient CreateOfflineHttp() => new(new StubHandler())
    {
        // Required so LocalizationService can resolve relative "locales/..." paths
        // before the stub handler intercepts them with a 404.
        BaseAddress = new Uri("http://localhost/")
    };

    [Fact]
    public async Task KeywordMode_MatchesTextAcrossMultipleEntities()
    {
        var js = new FakeJSRuntime();
        var store = new LocalFinanceStore(js);
        var loc = new LocalizationService(CreateOfflineHttp(), js);

        await loc.InitializeAsync();

        var alice = await store.AddClientAsync(new SalonClient { Name = "Alice", Notes = "prefers warm balayage" });
        await store.AddClientCommentAsync(new ClientComment
        {
            ClientId = alice.Id,
            Body = "Scalp feels itchy after new shampoo — mentioned switching"
        });

        await store.AddExpenseAsync(new Expense
        {
            Category = ExpenseCategory.Supplies,
            Date = DateTime.Today,
            Amount = 42m,
            Vendor = "Balayage Kit Supplier"
        });

        var search = new SemanticSearchService(js, store, loc);
        await search.SetModeAsync(SemanticSearchMode.Keyword);

        var hits = await search.QueryAsync("balayage");

        Assert.Contains(hits, h => h.EntityType == "client" && h.EntityId == alice.Id);
        Assert.Contains(hits, h => h.EntityType == "expense");
        Assert.All(hits, h => Assert.True(h.FallbackKeyword,
            "Every keyword-mode hit should be flagged as a keyword match."));

        await search.DisposeAsync();
    }

    [Fact]
    public async Task DisabledMode_ReturnsEmpty()
    {
        var js = new FakeJSRuntime();
        var store = new LocalFinanceStore(js);
        var loc = new LocalizationService(CreateOfflineHttp(), js);

        await loc.InitializeAsync();
        await store.AddClientAsync(new SalonClient { Name = "Alice", Notes = "balayage fan" });

        var search = new SemanticSearchService(js, store, loc);
        // Default mode is Disabled until the user opts in.
        Assert.Equal(SemanticSearchMode.Disabled, search.Mode);

        var hits = await search.QueryAsync("balayage");
        Assert.Empty(hits);

        await search.DisposeAsync();
    }

    [Fact]
    public async Task ShortQueries_AreIgnored()
    {
        var js = new FakeJSRuntime();
        var store = new LocalFinanceStore(js);
        var loc = new LocalizationService(CreateOfflineHttp(), js);

        await loc.InitializeAsync();
        await store.AddClientAsync(new SalonClient { Name = "Alice" });

        var search = new SemanticSearchService(js, store, loc);
        await search.SetModeAsync(SemanticSearchMode.Keyword);

        // Single character should never return hits — avoids firehose lookups.
        Assert.Empty(await search.QueryAsync("a"));
        Assert.Empty(await search.QueryAsync(string.Empty));
        Assert.Empty(await search.QueryAsync("   "));

        await search.DisposeAsync();
    }

    /// <summary>
    /// Returns 404 for every locale file so LocalizationService falls back to
    /// empty strings without any network IO. Keeps the tests fully offline.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
