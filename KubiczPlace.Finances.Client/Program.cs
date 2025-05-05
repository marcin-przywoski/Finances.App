using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System;
using Microsoft.Extensions.DependencyInjection;
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

        builder.Services.AddScoped<BrowserStorage>();
        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
        builder.Services.AddScoped<KubiczPlace.Finances.Services.IInvoiceService, KubiczPlace.Finances.Client.Services.InvoiceServiceHttp>();
        builder.Services.AddScoped<KubiczPlace.Finances.Services.IWorkerService, KubiczPlace.Finances.Client.Services.WorkerServiceHttp>();
        builder.Services.AddScoped<KubiczPlace.Finances.Services.IServiceItemService, KubiczPlace.Finances.Client.Services.ServiceItemServiceHttp>();
        builder.Services.AddScoped<KubiczPlace.Finances.Services.IServiceRecordService, KubiczPlace.Finances.Client.Services.ServiceRecordServiceHttp>();

        await builder.Build().RunAsync();
    }
}
