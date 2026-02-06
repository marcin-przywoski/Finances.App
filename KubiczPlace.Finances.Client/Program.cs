using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using KubiczPlace.Finances.Client.Services;

namespace KubiczPlace.Finances.Client;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        builder.Services.AddAuthorizationCore();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddAuthenticationStateDeserialization();

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
        builder.Services.AddScoped<ToastService>();
        builder.Services.AddScoped<WorkerContextService>();

        await builder.Build().RunAsync();
    }
}
