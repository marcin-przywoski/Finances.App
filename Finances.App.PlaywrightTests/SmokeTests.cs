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
            ServiceWorkers = ServiceWorkerPolicy.Allow,
            // Skip the toast slide-in animation so action buttons are
            // immediately clickable (the CSS honors prefers-reduced-motion).
            ReducedMotion = ReducedMotion.Reduce
        });

        var page = await context.NewPageAsync();
        return (server, playwright, browser, page);
    }

    private static async Task AddWorkerAsync(IPage page, string name, string commission)
    {
        await page.GotoAsync("/workers");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Workers" })).ToBeVisibleAsync(new() { Timeout = 30000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Worker" }).First.ClickAsync();
        await page.Locator("#worker-name").FillAsync(name);
        await page.Locator("#worker-commission").FillAsync(commission);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = name })).ToBeVisibleAsync();
    }

    private static async Task AddServiceAsync(IPage page, string name, string price)
    {
        await page.GotoAsync("/services");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Services" })).ToBeVisibleAsync(new() { Timeout = 30000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Add Service" }).First.ClickAsync();
        await page.Locator("#service-name").FillAsync(name);
        await page.Locator("#service-price").FillAsync(price);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = name })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Fresh_load_shows_onboarding_checklist_and_empty_workers()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        await page.GotoAsync("/");

        var firstRunCard = page.GetByTestId("first-run-card");
        await Expect(firstRunCard).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(firstRunCard.GetByRole(AriaRole.Link, new() { Name = "Add your workers" })).ToBeVisibleAsync();

        await page.GotoAsync("/workers");
        await Expect(page.GetByText("No workers yet")).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    [Fact]
    public async Task Onboarding_flow_records_first_visit_and_updates_dashboard()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        await AddWorkerAsync(page, "Anna Nowak", "45");
        await AddServiceAsync(page, "Strzyzenie", "50");

        await page.GotoAsync("/record");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Record a Service" })).ToBeVisibleAsync(new() { Timeout = 30000 });
        await page.Locator("#record-worker").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.Locator("#record-service").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Record" }).ClickAsync();
        await Expect(page.Locator(".toast-item", new() { HasText = "recorded successfully" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Service History" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Anna Nowak" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Dashboard" }).ClickAsync();
        var servicesDoneCard = page.Locator(".stat-card", new() { HasText = "Services Done" });
        await Expect(servicesDoneCard.Locator(".stat-value")).ToHaveTextAsync("1", new() { Timeout = 20000 });
    }

    [Fact]
    public async Task Export_erase_restore_and_import_round_trips_restore_data()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        await AddWorkerAsync(page, "Roundtrip Test Worker", "40");

        // Export a backup.
        await page.GetByRole(AriaRole.Link, new() { Name = "Import and Export" }).ClickAsync();
        var downloadTask = page.WaitForDownloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Export JSON" }).ClickAsync();
        var download = await downloadTask;
        var backupPath = Path.Combine(Path.GetTempPath(), $"finances-backup-{Guid.NewGuid():N}.json");
        await download.SaveAsAsync(backupPath);

        try
        {
            // Erase everything and confirm the worker is gone.
            await page.GetByRole(AriaRole.Button, new() { Name = "Erase All Data" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Erase Data" }).ClickAsync();
            await Expect(page.Locator(".toast-item", new() { HasText = "erased" }).Last).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Link, new() { Name = "Workers" }).ClickAsync();
            await Expect(page.GetByText("No workers yet")).ToBeVisibleAsync();

            // The erase kept a copy: restore it without touching the file.
            await page.GetByRole(AriaRole.Link, new() { Name = "Import and Export" }).ClickAsync();
            await page.GetByTestId("restore-previous").ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true }).ClickAsync();
            await Expect(page.Locator(".toast-item", new() { HasText = "restored" })).ToBeVisibleAsync();

            await page.GetByRole(AriaRole.Link, new() { Name = "Workers" }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Roundtrip Test Worker" })).ToBeVisibleAsync();

            // Erase again and restore from the exported file instead.
            await page.GetByRole(AriaRole.Link, new() { Name = "Import and Export" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Erase All Data" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Erase Data" }).ClickAsync();
            await Expect(page.Locator(".toast-item", new() { HasText = "erased" }).Last).ToBeVisibleAsync();

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

    [Fact]
    public async Task Theme_toggle_switches_to_dark_and_persists_across_reload()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        await page.GotoAsync("/");
        var toggle = page.GetByTestId("theme-toggle");
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 30000 });

        // Preference starts at auto (light in headless Chromium); one click = dark.
        await toggle.ClickAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark");

        // The pre-boot inline script must re-apply it on a full reload.
        await page.ReloadAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-bs-theme", "dark", new() { Timeout = 30000 });
    }

    [Fact]
    public async Task Deleting_a_record_can_be_undone_from_the_toast()
    {
        var (server, playwright, browser, page) = await StartAppAsync();
        await using var _ = server;
        using var __ = playwright;
        await using var ___ = browser;

        await AddWorkerAsync(page, "Anna Nowak", "45");
        await AddServiceAsync(page, "Strzyzenie", "50");

        await page.GotoAsync("/record");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Record a Service" })).ToBeVisibleAsync(new() { Timeout = 30000 });
        await page.Locator("#record-worker").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.Locator("#record-service").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Save Record" }).ClickAsync();
        await Expect(page.Locator(".toast-item", new() { HasText = "recorded successfully" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Service History" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Anna Nowak" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).Last.ClickAsync();
        await Expect(page.Locator(".toast-item", new() { HasText = "Record deleted" })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Cell, new() { Name = "Anna Nowak" })).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(1);
    }
}
