namespace KubiczPlace.Finances.PlaywrightTests;

[Collection("playwright")]
public class HomePageTests
{
    private const string BaseUrl = "http://localhost:8080";

    [Fact]
    public async Task Home_Should_Show_NavMenu()
    {
        var context = await PlaywrightFixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(BaseUrl + "/");
        Assert.Contains("Finances", await page.TitleAsync());
        Assert.True(await page.IsVisibleAsync("nav"));

        await context.CloseAsync();
    }
}
