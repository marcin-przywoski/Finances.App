using System.Collections;
using System.Globalization;
using System.Resources;
using Bunit;
using Finances.App.Client;
using Finances.App.Client.Layout;
using Finances.App.Client.Services;
using Finances.App.Client.Services.Storage;
using Finances.App.Client.Tests.TestDoubles;
using Finances.App.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Finances.App.Client.Tests;

/// <summary>
/// Localization contract: the Polish resources cover exactly the same keys as
/// the neutral English ones, the UI actually renders Polish under pl-PL, and
/// the language setting round-trips through the store.
/// </summary>
public class LocalizationTests
{
    private static HashSet<string> ResourceKeys(CultureInfo? specificCulture)
    {
        var manager = new ResourceManager("Finances.App.Client.Resources.AppStrings", typeof(AppStrings).Assembly);
        var set = specificCulture is null
            ? manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)
            : manager.GetResourceSet(specificCulture, true, false);
        Assert.NotNull(set);

        var keys = new HashSet<string>();
        foreach (DictionaryEntry entry in set!)
        {
            keys.Add((string)entry.Key);
        }

        return keys;
    }

    [Fact]
    public void Polish_resources_cover_exactly_the_english_key_set()
    {
        var english = ResourceKeys(null);
        var polish = ResourceKeys(new CultureInfo("pl"));

        Assert.NotEmpty(english);
        Assert.Empty(english.Except(polish));
        Assert.Empty(polish.Except(english));
    }

    [Fact]
    public void Validation_resources_cover_exactly_the_english_key_set()
    {
        var manager = new ResourceManager("Finances.App.Shared.ValidationStrings", typeof(ValidationStrings).Assembly);
        var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
        var polish = manager.GetResourceSet(new CultureInfo("pl"), true, false);
        Assert.NotNull(english);
        Assert.NotNull(polish);

        var englishKeys = english!.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        var polishKeys = polish!.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();

        Assert.NotEmpty(englishKeys);
        Assert.Equal(englishKeys, polishKeys);
    }

    [Fact]
    public void Validation_messages_follow_the_ui_culture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");
            Assert.Equal("To pole jest wymagane.", ValidationStrings.Required);

            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            Assert.Equal("This field is required.", ValidationStrings.Required);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task Language_setting_round_trips_and_rejects_unknown_values()
    {
        var store = new LocalFinanceStore(new InMemoryKeyValueStorage());

        await store.UpdateSettingsAsync(new AppSettings { Language = "pl" });
        Assert.Equal("pl", (await store.GetSettingsAsync()).Language);

        await store.UpdateSettingsAsync(new AppSettings { Language = "de" });
        Assert.Null((await store.GetSettingsAsync()).Language);
    }

    [Fact]
    public void Nav_menu_renders_polish_under_pl_culture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");
            using var ctx = new NavMenuRenderContext();

            var cut = ctx.Render<NavMenu>();

            Assert.Contains("Pulpit", cut.Markup);
            Assert.Contains("Pracownicy", cut.Markup);
            Assert.Contains("Wydatki", cut.Markup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Nav_menu_renders_english_under_invariant_culture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            using var ctx = new NavMenuRenderContext();

            var cut = ctx.Render<NavMenu>();

            Assert.Contains("Dashboard", cut.Markup);
            Assert.Contains("Workers", cut.Markup);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private sealed class NavMenuRenderContext : BunitContext
    {
        public NavMenuRenderContext()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddLocalization(options => options.ResourcesPath = "Resources");
        }
    }
}
