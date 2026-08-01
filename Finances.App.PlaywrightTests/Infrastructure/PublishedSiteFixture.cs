using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace Finances.App.PlaywrightTests.Infrastructure;

/// <summary>
/// Publishes the client twice (v1, then v2 with bumped release notes) once
/// per test run, and cleans its temp directories up afterwards. Serving real
/// publish output matters because the dev service worker is a no-op.
/// </summary>
public sealed class PublishedSiteFixture : IAsyncLifetime
{
    public const string UpdatedReleaseTitle = "Smoke-test update rollout";
    public const string UpdatedReleaseSummary = "Confirms that pending PWA updates expose human-readable release notes before reload.";

    private readonly List<string> _tempDirectories = [];

    public string FirstSiteRoot { get; private set; } = "";
    public string SecondSiteRoot { get; private set; } = "";
    public string InitialVersion { get; private set; } = "";
    public string UpdatedVersion { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sandboxRoot = CreatePublishSandbox(repositoryRoot);

        var firstPublish = await PublishClientAsync(sandboxRoot, "v1");
        FirstSiteRoot = ResolveSiteRoot(firstPublish);
        InitialVersion = ReadManifestVersion(FirstSiteRoot);

        PromoteSandboxRelease(sandboxRoot);
        var secondPublish = await PublishClientAsync(sandboxRoot, "v2");
        SecondSiteRoot = ResolveSiteRoot(secondPublish);
        UpdatedVersion = ReadManifestVersion(SecondSiteRoot);
    }

    public Task DisposeAsync()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not delete temp directory {directory}: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private static string FindRepositoryRoot()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "Finances.App.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root for the E2E tests.");
    }

    private string CreatePublishSandbox(string repositoryRoot)
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), $"finances-app-sandbox-{Guid.NewGuid():N}");
        _tempDirectories.Add(sandboxRoot);
        CopyProjectTree(Path.Combine(repositoryRoot, "Finances.App.Client"), Path.Combine(sandboxRoot, "Finances.App.Client"));
        CopyProjectTree(Path.Combine(repositoryRoot, "Finances.App.Shared"), Path.Combine(sandboxRoot, "Finances.App.Shared"));
        return sandboxRoot;
    }

    private async Task<string> PublishClientAsync(string sandboxRoot, string outputName)
    {
        var publishRoot = Path.Combine(Path.GetTempPath(), $"finances-app-pwa-{Guid.NewGuid():N}");
        _tempDirectories.Add(publishRoot);
        var publishDirectory = Path.Combine(publishRoot, outputName);
        Directory.CreateDirectory(publishDirectory);

        var projectPath = Path.Combine(sandboxRoot, "Finances.App.Client", "Finances.App.Client.csproj");
        var startInfo = new ProcessStartInfo("dotnet", $"publish \"{projectPath}\" -c Release -o \"{publishDirectory}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = sandboxRoot
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet publish for the E2E tests.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0)
        {
            return publishDirectory;
        }

        throw new InvalidOperationException($"dotnet publish failed for the E2E tests.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
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
            throw new InvalidOperationException("Could not read the service worker assets version.");
        }

        return match.Groups["version"].Value;
    }

    private static void PromoteSandboxRelease(string sandboxRoot)
    {
        var markerPath = Path.Combine(sandboxRoot, "Finances.App.Client", "wwwroot", "smoke-update.txt");
        var markerValue = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllText(markerPath, markerValue);

        var releasePath = Path.Combine(sandboxRoot, "Finances.App.Client", "wwwroot", "pwa-release.json");
        File.WriteAllText(releasePath, $$"""
        {
            "releaseId": "smoke-test-update",
            "publishedUtc": "{{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}}",
            "title": "{{UpdatedReleaseTitle}}",
            "summary": "{{UpdatedReleaseSummary}}",
            "changes": [
                "Publishes a second build for the Playwright smoke test.",
                "Keeps the installed build label unchanged until the update is applied.",
                "Shows the release note inside the App Updates card before reload."
            ]
        }
        """);
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
}

[CollectionDefinition("E2E")]
public sealed class E2ECollection : ICollectionFixture<PublishedSiteFixture>;
