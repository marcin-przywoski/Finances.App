namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Server-side implementation of IServiceItemService using EF Core.
/// </summary>
public class ServiceItemServiceDb(ApplicationDbContext db) : IServiceItemService
{
    public async Task<IReadOnlyList<ServiceItem>> GetAsync(CancellationToken ct = default)
        => await db.ServiceItems.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);

    public async Task<ServiceItem> AddAsync(ServiceItem item, CancellationToken ct = default)
    {
        db.ServiceItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task UpdateAsync(ServiceItem item, CancellationToken ct = default)
    {
        db.Entry(item).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.ServiceItems.FindAsync([id], ct);
        if (entity is null) return;
        db.ServiceItems.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
