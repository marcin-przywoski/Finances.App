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
        builder.Services.AddScoped<IFinanceService>(sp => sp.GetRequiredService<LocalFinanceStore>());
        builder.Services.AddScoped<ToastService>();
        builder.Services.AddScoped<WorkerContextService>();
        builder.Services.AddScoped<PwaUpdateService>();
        builder.Services.AddScoped<ThemeService>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddScoped<ExportService>();
        builder.Services.AddScoped<LocalizationService>();
        builder.Services.AddScoped<AttachmentStore>();

        await builder.Build().RunAsync();
    }
}
