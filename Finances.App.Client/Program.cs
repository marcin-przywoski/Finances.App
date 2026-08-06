using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Finances.App.Client.Services;
using Finances.App.Client.Services.Storage;

namespace Finances.App.Client;

static class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

        builder.Services.AddScoped<IKeyValueStorage, LocalStorageKeyValueStorage>();
        builder.Services.AddScoped<LocalFinanceStore>();
        builder.Services.AddScoped<IFinanceStore>(sp => sp.GetRequiredService<LocalFinanceStore>());
        builder.Services.AddScoped<IAnalyticsService>(sp => sp.GetRequiredService<LocalFinanceStore>());
        builder.Services.AddScoped<MoneyFormat>();
        builder.Services.AddScoped<ThemeService>();
        builder.Services.AddScoped<ToastService>();
        builder.Services.AddScoped<WorkerContextService>();
        builder.Services.AddScoped<PwaUpdateService>();

        var host = builder.Build();

        // Apply the persisted language before the first render; the load path
        // already copes with first-run and corrupt storage. "auto" keeps the
        // browser culture that Blazor detected.
        try
        {
            var store = host.Services.GetRequiredService<IFinanceStore>();
            var language = (await store.GetSettingsAsync()).Language;
            if (language is "pl" or "en")
            {
                var culture = new CultureInfo(language == "pl" ? "pl-PL" : "en-US");
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Language init failed: {ex.Message}");
        }

        await host.RunAsync();
    }
}
