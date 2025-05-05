namespace KubiczPlace.Finances.Client.Services;

using System.Net.Http.Json;
using KubiczPlace.Finances.Models;
using KubiczPlace.Finances.Services;
using KubiczPlace.Finances.Client.Services;

/// <summary>
/// WASM-side implementation: uses HttpClient to call minimal API.
/// </summary>
public class InvoiceServiceHttp(HttpClient http, BrowserStorage storage) : IInvoiceService
{
    const string CacheKey = "invoices";

    public async Task<IReadOnlyList<Invoice>> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var data = await http.GetFromJsonAsync<List<Invoice>>("/api/invoices", cancellationToken: ct);
            if (data is not null)
                await storage.SetAsync(CacheKey, data);
            return data ?? await storage.GetAsync<List<Invoice>>(CacheKey) ?? [];
        }
        catch
        {
            return await storage.GetAsync<List<Invoice>>(CacheKey) ?? [];
        }
    }

    public async Task UploadAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(pdf), "file", fileName);
        var resp = await http.PostAsync("/api/invoices", content, ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    public async Task ReprocessAsync(int id, CancellationToken ct = default)
    {
        var resp = await http.PostAsync($"/api/invoices/{id}/reprocess", null, ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    async Task UpdateCache(CancellationToken ct)
    {
        var data = await http.GetFromJsonAsync<List<Invoice>>("/api/invoices", cancellationToken: ct);
        if (data != null)
            await storage.SetAsync(CacheKey, data);
    }
}
