using Microsoft.Playwright;

namespace Finances.App.PlaywrightTests.Infrastructure;

public static class BrowserLauncher
{
    /// <summary>
    /// Launches the bundled Chromium. If the exact Playwright-pinned build is
    /// not installed (common on pre-provisioned machines), falls back to an
    /// explicit executable from PLAYWRIGHT_CHROMIUM_EXECUTABLE or the
    /// conventional pre-installed path.
    /// </summary>
    public static async Task<IBrowser> LaunchAsync(IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException)
        {
            var executable = Environment.GetEnvironmentVariable("PLAYWRIGHT_CHROMIUM_EXECUTABLE");
            if (string.IsNullOrEmpty(executable) && File.Exists("/opt/pw-browsers/chromium"))
            {
                executable = "/opt/pw-browsers/chromium";
            }

            if (string.IsNullOrEmpty(executable))
            {
                throw;
            }

            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = executable
            });
        }
    }
}
