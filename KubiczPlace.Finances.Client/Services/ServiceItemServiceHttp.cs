namespace KubiczPlace.Finances.Client.Services;

using System.Net.Http.Json;
using KubiczPlace.Finances.Shared.Models;
using KubiczPlace.Finances.Shared.Services;
using KubiczPlace.Finances.Client.Services;

public class ServiceItemServiceHttp(HttpClient http, BrowserStorage storage) : IServiceItemService
{
    const string CacheKey = "serviceitems";

    public async Task<IReadOnlyList<ServiceItem>> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var data = await http.GetFromJsonAsync<List<ServiceItem>>("/api/serviceitems", cancellationToken: ct);
            if (data is not null)
                await storage.SetAsync(CacheKey, data);
            return data ?? await storage.GetAsync<List<ServiceItem>>(CacheKey) ?? [];
        }
        catch
        {
            return await storage.GetAsync<List<ServiceItem>>(CacheKey) ?? [];
        }
    }

    public async Task<ServiceItem> AddAsync(ServiceItem item, CancellationToken ct = default)
    {
        var added = (await http.PostAsJsonAsync("/api/serviceitems", item, ct)).Content.ReadFromJsonAsync<ServiceItem>(cancellationToken: ct).Result!;
        await UpdateCache(ct);
        return added;
    }

    public async Task UpdateAsync(ServiceItem item, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"/api/serviceitems/{item.Id}", item, ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"/api/serviceitems/{id}", ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    async Task UpdateCache(CancellationToken ct)
    {
        var data = await http.GetFromJsonAsync<List<ServiceItem>>("/api/serviceitems", cancellationToken: ct);
        if (data != null)
            await storage.SetAsync(CacheKey, data);
    }
}
