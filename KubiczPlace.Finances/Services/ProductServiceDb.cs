using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Shared.Models;
using Microsoft.EntityFrameworkCore;
using KubiczPlace.Finances.Shared.Services;

namespace KubiczPlace.Finances.Services;

public class ProductServiceDb(ApplicationDbContext db) : IProductService
{
    public async Task<IReadOnlyList<Product>> GetAsync(CancellationToken ct = default)
        => await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product;
    }

    public async Task UpdateAsync(Product product, CancellationToken ct = default)
    {
        db.Entry(product).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Products.FindAsync([id], ct);
        if (entity is null) return;
        db.Products.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
