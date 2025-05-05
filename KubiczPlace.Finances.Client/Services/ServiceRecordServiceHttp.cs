namespace KubiczPlace.Finances.Client.Services;

using System.Net.Http.Json;
using KubiczPlace.Finances.Models;
using KubiczPlace.Finances.Services;
using KubiczPlace.Finances.Client.Services;

public class ServiceRecordServiceHttp(HttpClient http, BrowserStorage storage) : IServiceRecordService
{
    const string CacheKey = "servicerecords";

    public async Task<IReadOnlyList<ServiceRecord>> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var data = await http.GetFromJsonAsync<List<ServiceRecord>>("/api/servicerecords", cancellationToken: ct);
            if (data is not null)
                await storage.SetAsync(CacheKey, data);
            return data ?? await storage.GetAsync<List<ServiceRecord>>(CacheKey) ?? [];
        }
        catch
        {
            return await storage.GetAsync<List<ServiceRecord>>(CacheKey) ?? [];
        }
    }

    public async Task<ServiceRecord> AddAsync(ServiceRecord rec, CancellationToken ct = default)
    {
        var added = (await http.PostAsJsonAsync("/api/servicerecords", rec, ct)).Content.ReadFromJsonAsync<ServiceRecord>(cancellationToken: ct).Result!;
        await UpdateCache(ct);
        return added;
    }

    public async Task UpdateAsync(ServiceRecord rec, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"/api/servicerecords/{rec.Id}", rec, ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"/api/servicerecords/{id}", ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    async Task UpdateCache(CancellationToken ct)
    {
        var data = await http.GetFromJsonAsync<List<ServiceRecord>>("/api/servicerecords", cancellationToken: ct);
        if (data != null)
            await storage.SetAsync(CacheKey, data);
    }
}
