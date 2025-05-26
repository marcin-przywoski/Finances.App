namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Shared.Models;
using Microsoft.EntityFrameworkCore;
using KubiczPlace.Finances.Shared.Services;

/// <summary>
/// Server-side implementation of IWorkerService that operates directly on the DbContext.
/// </summary>
public class WorkerServiceDb(ApplicationDbContext db) : IWorkerService
{
    public async Task<IReadOnlyList<Worker>> GetAsync(CancellationToken ct = default)
        => await db.Workers.AsNoTracking().OrderBy(w => w.Name).ToListAsync(ct);

    public async Task<Worker> AddAsync(Worker worker, CancellationToken ct = default)
    {
        db.Workers.Add(worker);
        await db.SaveChangesAsync(ct);
        return worker;
    }

    public async Task UpdateAsync(Worker worker, CancellationToken ct = default)
    {
        db.Entry(worker).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Workers.FindAsync([id], ct);
        if (entity is null) return;
        db.Workers.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
