using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Models;
using Microsoft.EntityFrameworkCore;

namespace KubiczPlace.Finances.Services;

public class ProductSaleServiceDb(ApplicationDbContext db) : IProductSaleService
{
    public async Task<IReadOnlyList<ProductSale>> GetAsync(CancellationToken ct = default)
        => await db.ProductSales.Include(s => s.Product).Include(s => s.Worker)
            .AsNoTracking().OrderByDescending(s => s.Date).ToListAsync(ct);

    public async Task<ProductSale> AddAsync(ProductSale sale, CancellationToken ct = default)
    {
        // adjust stock
        var product = await db.Products.FindAsync([sale.ProductId], ct) ?? throw new InvalidOperationException("product not found");
        if (product.QuantityInStock <= 0)
            throw new InvalidOperationException("Out of stock");
        product.QuantityInStock--;

        db.ProductSales.Add(sale);
        await db.SaveChangesAsync(ct);
        return sale;
    }

    public async Task UpdateAsync(ProductSale sale, CancellationToken ct = default)
    {
        var existing = await db.ProductSales.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sale.Id, ct);
        if (existing is null) throw new InvalidOperationException("sale not found");
        if (existing.ProductId != sale.ProductId)
        {
            // revert stock on old product, deduct on new
            var oldProd = await db.Products.FindAsync([existing.ProductId], ct);
            var newProd = await db.Products.FindAsync([sale.ProductId], ct);
            if (oldProd is not null) oldProd.QuantityInStock++;
            if (newProd is not null) newProd.QuantityInStock--;
        }
        db.Entry(sale).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.ProductSales.FindAsync([id], ct);
        if (entity is null) return;
        var prod = await db.Products.FindAsync([entity.ProductId], ct);
        if (prod is not null) prod.QuantityInStock++;
        db.ProductSales.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
