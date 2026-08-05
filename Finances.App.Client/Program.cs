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

        builder.Services.AddScoped<IKeyValueStorage, LocalStorageKeyValueStorage>();
        builder.Services.AddScoped<LocalFinanceStore>();
        builder.Services.AddScoped<IFinanceStore>(sp => sp.GetRequiredService<LocalFinanceStore>());
        builder.Services.AddScoped<IAnalyticsService>(sp => sp.GetRequiredService<LocalFinanceStore>());
        builder.Services.AddScoped<MoneyFormat>();
        builder.Services.AddScoped<ThemeService>();
        builder.Services.AddScoped<ToastService>();
        builder.Services.AddScoped<WorkerContextService>();
        builder.Services.AddScoped<PwaUpdateService>();

        await builder.Build().RunAsync();
    }
}
