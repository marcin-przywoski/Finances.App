using KubiczPlace.Finances.Models;

namespace KubiczPlace.Finances.Services;

public interface IProductSaleService
{
    Task<IReadOnlyList<ProductSale>> GetAsync(CancellationToken ct = default);
    Task<ProductSale> AddAsync(ProductSale sale, CancellationToken ct = default);
    Task UpdateAsync(ProductSale sale, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
