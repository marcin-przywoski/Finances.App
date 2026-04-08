using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Finances.App.Client.Services;

namespace Finances.App.Client;

static class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        builder.Services.AddScoped<LocalFinanceStore>();
        builder.Services.AddScoped<LocalApiMessageHandler>();
        builder.Services.AddScoped(sp => new HttpClient(sp.GetRequiredService<LocalApiMessageHandler>())
        {
            BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
        });
        builder.Services.AddScoped<ToastService>();
        builder.Services.AddScoped<WorkerContextService>();
        builder.Services.AddScoped<PwaUpdateService>();

        await builder.Build().RunAsync();
    }
}
