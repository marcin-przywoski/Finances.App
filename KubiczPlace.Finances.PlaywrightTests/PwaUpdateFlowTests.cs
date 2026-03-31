using System.Diagnostics;
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
        var publishDirectory = await PublishClientAsync(repositoryRoot);
        var siteRoot = ResolveSiteRoot(publishDirectory);
        var initialVersion = ReadManifestVersion(siteRoot);

        await using var server = new StaticSiteServer(siteRoot);
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

        var updatedVersion = PromotePublishedVersion(siteRoot, initialVersion);

        await page.GetByRole(AriaRole.Button, new() { Name = "Check for updates" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Reload to update" }).WaitForAsync();

        Assert.Equal(initialVersion, (await currentBuildValue.InnerTextAsync()).Trim());

        await page.GetByRole(AriaRole.Button, new() { Name = "Reload to update" }).ClickAsync();
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

    private static async Task<string> PublishClientAsync(string repositoryRoot)
    {
        var publishDirectory = Path.Combine(Path.GetTempPath(), $"kubiczplace-finances-pwa-{Guid.NewGuid():N}");
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

    private static string PromotePublishedVersion(string siteRoot, string currentVersion)
    {
        var assetsManifestPath = Path.Combine(siteRoot, "service-worker-assets.js");
        var nextVersion = $"smoke-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var contents = File.ReadAllText(assetsManifestPath);

        var versionRegex = new Regex("(\"version\"\\s*:\\s*\")(?<version>[^\"]+)(\")");
        var updatedContents = versionRegex.Replace(
            contents,
            match => $"{match.Groups[1].Value}{nextVersion}{match.Groups[3].Value}",
            1);

        if (updatedContents == contents || !updatedContents.Contains(nextVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The smoke test could not replace service worker version '{currentVersion}'.");
        }

        File.WriteAllText(assetsManifestPath, updatedContents);
        return nextVersion;
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
}