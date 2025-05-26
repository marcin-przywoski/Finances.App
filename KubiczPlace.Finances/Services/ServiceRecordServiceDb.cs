namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Shared.Models;
using Microsoft.EntityFrameworkCore;
using KubiczPlace.Finances.Shared.Services;

/// <summary>
/// Server-side implementation of IServiceRecordService using EF Core.
/// </summary>
public class ServiceRecordServiceDb(ApplicationDbContext db) : IServiceRecordService
{
    public async Task<IReadOnlyList<ServiceRecord>> GetAsync(CancellationToken ct = default)
        => await db.ServiceRecords.AsNoTracking()
            .Include(r => r.Worker)
            .Include(r => r.ServiceItem)
            .OrderByDescending(r => r.Date)
            .ToListAsync(ct);

    public async Task<ServiceRecord> AddAsync(ServiceRecord rec, CancellationToken ct = default)
    {
        db.ServiceRecords.Add(rec);
        await db.SaveChangesAsync(ct);
        return rec;
    }

    public async Task UpdateAsync(ServiceRecord rec, CancellationToken ct = default)
    {
        db.Entry(rec).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.ServiceRecords.FindAsync([id], ct);
        if (entity is null) return;
        db.ServiceRecords.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
