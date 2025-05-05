using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using KubiczPlace.Finances.Client.Pages;
using KubiczPlace.Finances.Components;
using KubiczPlace.Finances.Components.Account;
using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Services;
using KubiczPlace.Finances.Models;
using ClosedXML.Excel;

namespace KubiczPlace.Finances;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents()
            .AddAuthenticationStateSerialization();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityUserAccessor>();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        builder.Services.AddScoped<IInvoiceProcessor, TesseractInvoiceProcessor>();
        builder.Services.AddScoped<IInvoiceService, InvoiceServiceDb>();
        builder.Services.AddScoped<IWorkerService, WorkerServiceDb>();
        builder.Services.AddScoped<IServiceItemService, ServiceItemServiceDb>();
        builder.Services.AddScoped<IServiceRecordService, ServiceRecordServiceDb>();
        builder.Services.AddScoped<IProductService, ProductServiceDb>();
        builder.Services.AddScoped<IProductSaleService, ProductSaleServiceDb>();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseWebAssemblyDebugging();
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(Client._Imports).Assembly);

        // Add additional endpoints required by the Identity /Account Razor components.
        app.MapAdditionalIdentityEndpoints();

        // Invoice file download
        app.MapGet("/invoices/{id:int}/file", async (int id, ApplicationDbContext db) =>
        {
            var inv = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            return inv is null
                ? Results.NotFound()
                : Results.File(inv.Content, "application/pdf", inv.FileName);
        });

        // Invoice API endpoints for WASM client
        app.MapGet("/api/invoices", async (IInvoiceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)));

        app.MapPost("/api/invoices", async (IFormFile file, IInvoiceService svc, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0) return Results.BadRequest("file missing");
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            await svc.UploadAsync(ms, file.FileName, ct);
            return Results.Ok();
        });

        app.MapPost("/api/invoices/{id:int}/reprocess", async (int id, IInvoiceService svc, CancellationToken ct) =>
        {
            await svc.ReprocessAsync(id, ct);
            return Results.Ok();
        });

        // Worker API endpoints
        app.MapGet("/api/workers", async (IWorkerService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)));

        app.MapPost("/api/workers", async (Worker worker, IWorkerService svc, CancellationToken ct) => Results.Ok(await svc.AddAsync(worker, ct)));

        app.MapPut("/api/workers/{id:int}", async (int id, Worker worker, IWorkerService svc, CancellationToken ct) =>
        {
            if (id != worker.Id) return Results.BadRequest("id mismatch");
            await svc.UpdateAsync(worker, ct);
            return Results.Ok();
        });

        app.MapDelete("/api/workers/{id:int}", async (int id, IWorkerService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.Ok();
        });

        // ServiceItem API endpoints
        app.MapGet("/api/serviceitems", async (IServiceItemService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)));
        app.MapPost("/api/serviceitems", async (ServiceItem item, IServiceItemService svc, CancellationToken ct) => Results.Ok(await svc.AddAsync(item, ct)));
        app.MapPut("/api/serviceitems/{id:int}", async (int id, ServiceItem item, IServiceItemService svc, CancellationToken ct) =>
        {
            if (id != item.Id) return Results.BadRequest("id mismatch");
            await svc.UpdateAsync(item, ct);
            return Results.Ok();
        });
        app.MapDelete("/api/serviceitems/{id:int}", async (int id, IServiceItemService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.Ok();
        });

        // ServiceRecord API endpoints
        app.MapGet("/api/servicerecords", async (IServiceRecordService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)));
        app.MapPost("/api/servicerecords", async (ServiceRecord rec, IServiceRecordService svc, CancellationToken ct) => Results.Ok(await svc.AddAsync(rec, ct)));
        app.MapPut("/api/servicerecords/{id:int}", async (int id, ServiceRecord rec, IServiceRecordService svc, CancellationToken ct) =>
        {
            if (id != rec.Id) return Results.BadRequest("id mismatch");
            await svc.UpdateAsync(rec, ct);
            return Results.Ok();
        });
        app.MapDelete("/api/servicerecords/{id:int}", async (int id, IServiceRecordService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.Ok();
        });

        // Product API endpoints
        app.MapGet("/api/products", async (IProductService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)));
        app.MapPost("/api/products", async (Product p, IProductService svc, CancellationToken ct) => Results.Ok(await svc.AddAsync(p, ct)));
        app.MapPut("/api/products/{id:int}", async (int id, Product p, IProductService svc, CancellationToken ct) =>
        {
            if (id != p.Id) return Results.BadRequest("id mismatch");
            await svc.UpdateAsync(p, ct);
            return Results.Ok();
        });
        app.MapDelete("/api/products/{id:int}", async (int id, IProductService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.Ok();
        });

        // ProductSales API endpoints
        app.MapGet("/api/productsales", async (IProductSaleService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)));
        app.MapPost("/api/productsales", async (ProductSale sale, IProductSaleService svc, CancellationToken ct) => Results.Ok(await svc.AddAsync(sale, ct)));
        app.MapPut("/api/productsales/{id:int}", async (int id, ProductSale sale, IProductSaleService svc, CancellationToken ct) =>
        {
            if (id != sale.Id) return Results.BadRequest("id mismatch");
            await svc.UpdateAsync(sale, ct);
            return Results.Ok();
        });
        app.MapDelete("/api/productsales/{id:int}", async (int id, IProductSaleService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.Ok();
        });

        // Excel export endpoints
        app.MapGet("/export/products", async (IProductService svc) =>
        {
            var products = await svc.GetAsync();
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Products");
            ws.Cell(1, 1).InsertTable(products.Select(p => new
            {
                p.Name,
                p.PurchasePrice,
                p.SellPrice,
                p.QuantityInStock,
                p.DefaultSharePct
            }));
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return Results.File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "products.xlsx");
        });

        app.MapGet("/export/productsales", async (IProductSaleService svc) =>
        {
            var sales = await svc.GetAsync();
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Sales");
            ws.Cell(1, 1).InsertTable(sales.Select(s => new
            {
                s.Date,
                Product = s.Product!.Name,
                Worker = s.Worker!.Name,
                s.PriceSold,
                s.WorkerSharePct,
                s.WorkerEarnings
            }));
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return Results.File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "product_sales.xlsx");
        });

        app.Run();
    }
}
