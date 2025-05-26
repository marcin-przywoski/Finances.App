namespace KubiczPlace.Finances.Client.Services;

using System.Net.Http.Json;
using KubiczPlace.Finances.Shared.Models;
using KubiczPlace.Finances.Shared.Services;
using KubiczPlace.Finances.Client.Services;

public class WorkerServiceHttp(HttpClient http, BrowserStorage storage) : IWorkerService
{
    const string CacheKey = "workers";

    public async Task<IReadOnlyList<Worker>> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var data = await http.GetFromJsonAsync<List<Worker>>("/api/workers", cancellationToken: ct);
            if (data is not null)
                await storage.SetAsync(CacheKey, data);
            return data ?? await storage.GetAsync<List<Worker>>(CacheKey) ?? [];
        }
        catch
        {
            return await storage.GetAsync<List<Worker>>(CacheKey) ?? [];
        }
    }

    public async Task<Worker> AddAsync(Worker worker, CancellationToken ct = default)
    {
        var added = (await http.PostAsJsonAsync("/api/workers", worker, ct)).Content.ReadFromJsonAsync<Worker>(cancellationToken: ct).Result!;
        await UpdateCache(ct);
        return added;
    }

    public async Task UpdateAsync(Worker worker, CancellationToken ct = default)
    {
        var resp = await http.PutAsJsonAsync($"/api/workers/{worker.Id}", worker, ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var resp = await http.DeleteAsync($"/api/workers/{id}", ct);
        resp.EnsureSuccessStatusCode();
        await UpdateCache(ct);
    }

    async Task UpdateCache(CancellationToken ct)
    {
        var data = await http.GetFromJsonAsync<List<Worker>>("/api/workers", cancellationToken: ct);
        if (data != null)
            await storage.SetAsync(CacheKey, data);
    }
}
