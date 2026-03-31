using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using KubiczPlace.Finances.PlaywrightTests.Infrastructure;
using Microsoft.Playwright;
using Xunit;

namespace KubiczPlace.Finances.PlaywrightTests;

public sealed class PwaUpdateFlowTests
{
    [Fact]
    public async Task Update_check_keeps_installed_build_until_reload_and_switches_after_apply()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sandboxRoot = CreatePublishSandbox(repositoryRoot);
        var firstPublishDirectory = await PublishClientAsync(sandboxRoot, "v1");
        var firstSiteRoot = ResolveSiteRoot(firstPublishDirectory);
        var initialVersion = ReadManifestVersion(firstSiteRoot);

        PromoteSandboxRelease(sandboxRoot);
        var secondPublishDirectory = await PublishClientAsync(sandboxRoot, "v2");
        var secondSiteRoot = ResolveSiteRoot(secondPublishDirectory);
        var updatedVersion = ReadManifestVersion(secondSiteRoot);

        await using var server = new StaticSiteServer(firstSiteRoot);
        server.Start();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = server.BaseUri.ToString(),
            ServiceWorkers = ServiceWorkerPolicy.Allow
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForFunctionAsync("() => navigator.serviceWorker?.controller !== null");
        await page.GetByRole(AriaRole.Link, new() { Name = "Import and Export" }).ClickAsync();
        await page.WaitForURLAsync("**/data");

        var currentBuildValue = page.Locator("dl.row dd").First;
        await WaitForTextAsync(currentBuildValue, initialVersion);

        server.SwitchSiteRoot(secondSiteRoot);

        await page.GetByRole(AriaRole.Button, new() { Name = "Check for updates" }).ClickAsync();

        var reloadButton = page.GetByRole(AriaRole.Button, new() { Name = "Reload to update" });
        try
        {
            await reloadButton.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30000
            });
        }
        catch (TimeoutException ex)
        {
            var diagnostics = await ReadUpdateDiagnosticsAsync(page);
            throw new Xunit.Sdk.XunitException($"The update button never appeared. Diagnostics: {diagnostics}", ex);
        }

        Assert.Equal(initialVersion, (await currentBuildValue.InnerTextAsync()).Trim());

        await reloadButton.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await WaitForTextAsync(currentBuildValue, updatedVersion);
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Channel = "msedge",
                Headless = true
            });
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException("Unable to launch Microsoft Edge for the PWA smoke test.", ex);
        }
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "KubiczPlace.Finances.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root for the PWA smoke test.");
    }

    private static async Task<string> PublishClientAsync(string repositoryRoot, string outputName)
    {
        var publishDirectory = Path.Combine(Path.GetTempPath(), $"kubiczplace-finances-pwa-{Guid.NewGuid():N}", outputName);
        Directory.CreateDirectory(publishDirectory);

        var projectPath = Path.Combine(repositoryRoot, "KubiczPlace.Finances.Client", "KubiczPlace.Finances.Client.csproj");
        var startInfo = new ProcessStartInfo("dotnet", $"publish \"{projectPath}\" -c Release -o \"{publishDirectory}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet publish for the PWA smoke test.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            return publishDirectory;
        }

        throw new InvalidOperationException($"dotnet publish failed for the PWA smoke test.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static string CreatePublishSandbox(string repositoryRoot)
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), $"kubiczplace-finances-sandbox-{Guid.NewGuid():N}");
        CopyProjectTree(Path.Combine(repositoryRoot, "KubiczPlace.Finances.Client"), Path.Combine(sandboxRoot, "KubiczPlace.Finances.Client"));
        CopyProjectTree(Path.Combine(repositoryRoot, "KubiczPlace.Finances.Shared"), Path.Combine(sandboxRoot, "KubiczPlace.Finances.Shared"));
        return sandboxRoot;
    }

    private static string ResolveSiteRoot(string publishDirectory)
    {
        var wwwrootPath = Path.Combine(publishDirectory, "wwwroot");
        return File.Exists(Path.Combine(wwwrootPath, "index.html"))
            ? wwwrootPath
            : publishDirectory;
    }

    private static string ReadManifestVersion(string siteRoot)
    {
        var assetsManifestPath = Path.Combine(siteRoot, "service-worker-assets.js");
        var contents = File.ReadAllText(assetsManifestPath);
        var match = Regex.Match(contents, "\"version\"\\s*:\\s*\"(?<version>[^\"]+)\"");

        if (!match.Success)
        {
            throw new InvalidOperationException("The smoke test could not read the current service worker assets version.");
        }

        return match.Groups["version"].Value;
    }

    private static void PromoteSandboxRelease(string sandboxRoot)
    {
        var markerPath = Path.Combine(sandboxRoot, "KubiczPlace.Finances.Client", "wwwroot", "smoke-update.txt");
        var markerValue = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllText(markerPath, markerValue);
    }

    private static void CopyProjectTree(string sourcePath, string destinationPath)
    {
        var sourceDirectory = new DirectoryInfo(sourcePath);
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in sourceDirectory.GetDirectories())
        {
            if (directory.Name is "bin" or "obj")
            {
                continue;
            }

            CopyProjectTree(directory.FullName, Path.Combine(destinationPath, directory.Name));
        }

        foreach (var file in sourceDirectory.GetFiles())
        {
            file.CopyTo(Path.Combine(destinationPath, file.Name), true);
        }
    }

    private static async Task WaitForTextAsync(ILocator locator, string expectedText)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            var actualText = (await locator.InnerTextAsync()).Trim();
            if (string.Equals(actualText, expectedText, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(250);
        }

        var finalText = (await locator.InnerTextAsync()).Trim();
        throw new Xunit.Sdk.XunitException($"Expected text '{expectedText}' but found '{finalText}'.");
    }

    private static async Task<string> ReadUpdateDiagnosticsAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(@"async () => {
            const registration = await navigator.serviceWorker.getRegistration();
            const values = Array.from(document.querySelectorAll('dl.row dd')).map(node => node.textContent?.trim() ?? null);

            return JSON.stringify({
                hasController: Boolean(navigator.serviceWorker?.controller),
                waitingState: registration?.waiting?.state ?? null,
                installingState: registration?.installing?.state ?? null,
                activeState: registration?.active?.state ?? null,
                buildValue: values[0] ?? null,
                lastCheckedValue: values[1] ?? null,
                lastAppliedValue: values[2] ?? null,
                statusBadge: document.querySelector('.badge')?.textContent?.trim() ?? null,
                bodyText: document.body.innerText
            });
        }");
    }
}