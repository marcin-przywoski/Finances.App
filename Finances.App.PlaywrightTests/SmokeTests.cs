using Finances.App.PlaywrightTests.Infrastructure;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Finances.App.PlaywrightTests;

/// <summary>
/// End-to-end smokes against real published output. The published site runs
/// with IL trimming, so these also guard the snapshot serialization contract.
/// </summary>
[Collection("E2E")]
[Trait("Category", "E2E")]
public sealed class SmokeTests
{
    private readonly PublishedSiteFixture _site;

    public SmokeTests(PublishedSiteFixture site)
    {
        _site = site;
    }

    private async Task<(StaticSiteServer Server, IPlaywright Playwright, IBrowser Browser, IPage Page)> StartAppAsync()
    {
        var server = new StaticSiteServer(_site.FirstSiteRoot);
        server.Start();

        var playwright = await Playwright.CreateAsync();
        var browser = await BrowserLauncher.LaunchAsync(playwright);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = server.BaseUri.ToString(),
            ServiceWorkers = ServiceWorkerPolicy.Allow
        });

        var page = await context.NewPageAsync();
        return (server, playwright, browser, page);
    }

    [Fact]
    public async Task Fresh_load_seeds_default_workers()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        await page.GotoAsync("/workers");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workers" })).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Jan Kowalski" })).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task Recording_a_service_updates_the_dashboard()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        await page.GotoAsync("/record");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Record a Service" })).ToBeVisibleAsync(new() { Timeout = 30000 });

        await page.Locator("#record-worker").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.Locator("#record-service").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Record" }).ClickAsync();

        await Expect(page.Locator(".toast-item", new() { HasText = "recorded successfully" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Dashboard" }).ClickAsync();
        var servicesDoneCard = page.Locator(".stat-card", new() { HasText = "Services Done" });
        await Expect(servicesDoneCard.Locator(".stat-value")).ToHaveTextAsync("1", new() { Timeout = 20000 });
    }

    [Fact]
    public async Task Export_reset_import_round_trip_restores_data()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        // Create a distinctive worker.
        await page.GotoAsync("/workers");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workers" })).ToBeVisibleAsync(new() { Timeout = 30000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Worker" }).First.ClickAsync();
        await page.Locator("#worker-name").FillAsync("Roundtrip Test Worker");
        await page.Locator("#worker-commission").FillAsync("40");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Roundtrip Test Worker" })).ToBeVisibleAsync();

        // Export a backup.
        await page.GetByRole(AriaRole.Link, new() { Name = "Import and Export" }).ClickAsync();
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Export JSON" }).ClickAsync();
        var download = await downloadTask;
        var backupPath = Path.Combine(Path.GetTempPath(), $"finances-backup-{Guid.NewGuid():N}.json");
        await download.SaveAsAsync(backupPath);

        try
        {
            // Reset to starter data and confirm the worker is gone.
            await page.GetByRole(AriaRole.Button, new() { Name = "Reset Starter Data" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Reset Data" }).ClickAsync();
            await Expect(page.Locator(".toast-item", new() { HasText = "reset" })).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Link, new() { Name = "Workers" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Roundtrip Test Worker" })).Not.ToBeVisibleAsync();

            // Import the backup and confirm the worker is back.
            await page.GetByRole(AriaRole.Link, new() { Name = "Import and Export" }).ClickAsync();
            await page.Locator("input[type=file]").SetInputFilesAsync(backupPath);
            await Expect(page.Locator(".toast-item", new() { HasText = "imported" })).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Link, new() { Name = "Workers" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Roundtrip Test Worker" })).ToBeVisibleAsync();
        }
        finally
        {
            File.Delete(backupPath);
        }
    }
}
