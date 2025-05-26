using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System;
using Microsoft.Extensions.DependencyInjection;
using KubiczPlace.Finances.Client.Services;
using KubiczPlace.Finances.Shared.Services;

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
        builder.Services.AddScoped<IInvoiceService, InvoiceServiceHttp>();
        builder.Services.AddScoped<IWorkerService, WorkerServiceHttp>();
        builder.Services.AddScoped<IServiceItemService, ServiceItemServiceHttp>();
        builder.Services.AddScoped<IServiceRecordService, ServiceRecordServiceHttp>();

        await builder.Build().RunAsync();
    }
}
