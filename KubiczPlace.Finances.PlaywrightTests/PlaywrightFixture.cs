namespace KubiczPlace.Finances.PlaywrightTests;

public class PlaywrightFixture : IAsyncLifetime
{
    public static IBrowser Browser { get; private set; } = default!;
    private IPlaywright _pw = default!;

    public async Task InitializeAsync()
    {
        _pw = await Playwright.CreateAsync();
        Browser = await _pw.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        _pw.Dispose();
    }
}
