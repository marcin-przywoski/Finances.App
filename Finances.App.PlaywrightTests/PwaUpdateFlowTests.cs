using Finances.App.PlaywrightTests.Infrastructure;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Finances.App.PlaywrightTests;

[Collection("E2E")]
[Trait("Category", "E2E")]
public sealed class PwaUpdateFlowTests
{
    private readonly PublishedSiteFixture _site;

    public PwaUpdateFlowTests(PublishedSiteFixture site)
    {
        _site = site;
    }

    [Fact]
    public async Task Update_check_keeps_installed_build_until_reload_and_switches_after_apply()
    {
        await using var server = new StaticSiteServer(_site.FirstSiteRoot);
        server.Start();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await BrowserLauncher.LaunchAsync(playwright);
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = server.BaseUri.ToString(),
            ServiceWorkers = ServiceWorkerPolicy.Allow
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForFunctionAsync("() => !!(navigator.serviceWorker && navigator.serviceWorker.controller)");
        await page.GetByRole(AriaRole.Link, new() { Name = "Data & Backups" }).ClickAsync();
        await page.WaitForURLAsync("**/data");

        var installedBuild = page.GetByTestId("installed-build");
        await Expect(installedBuild).ToHaveTextAsync(_site.InitialVersion, new() { Timeout = 20000 });

        server.SwitchSiteRoot(_site.SecondSiteRoot);

        await page.GetByRole(AriaRole.Button, new() { Name = "Check for updates" }).ClickAsync();

        var reloadButton = page.GetByRole(AriaRole.Button, new() { Name = "Reload to update" });
        try
        {
            await Expect(reloadButton).ToBeVisibleAsync(new() { Timeout = 30000 });
        }
        catch (PlaywrightException ex)
        {
            var diagnostics = await ReadUpdateDiagnosticsAsync(page);
            throw new Xunit.Sdk.XunitException($"The update button never appeared. Diagnostics: {diagnostics}", ex);
        }

        await Expect(page.GetByText(PublishedSiteFixture.UpdatedReleaseTitle, new() { Exact = true }).First).ToBeVisibleAsync();

        // The installed build must not change until the update is applied.
        await Expect(installedBuild).ToHaveTextAsync(_site.InitialVersion);

        await reloadButton.ClickAsync();
        await Expect(installedBuild).ToHaveTextAsync(_site.UpdatedVersion, new() { Timeout = 30000 });
    }

    private static async Task<string> ReadUpdateDiagnosticsAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(@"async () => {
            const registration = await navigator.serviceWorker.getRegistration();
            const dds = Object.fromEntries(Array.from(document.querySelectorAll('dl.row dd'))
                .map((node, index) => [node.dataset.testid ?? `dd-${index}`, node.textContent?.trim() ?? null]));

            return JSON.stringify({
                hasController: !!(navigator.serviceWorker && navigator.serviceWorker.controller),
                waitingState: registration?.waiting?.state ?? null,
                installingState: registration?.installing?.state ?? null,
                activeState: registration?.active?.state ?? null,
                dds,
                statusBadge: document.querySelector('.badge')?.textContent?.trim() ?? null
            });
        }");
    }
}
