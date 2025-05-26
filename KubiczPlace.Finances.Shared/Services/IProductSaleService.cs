using KubiczPlace.Finances.Shared.Models;

namespace KubiczPlace.Finances.Shared.Services;

public interface IProductSaleService
{
    Task<IReadOnlyList<ProductSale>> GetAsync(CancellationToken ct = default);
    Task<ProductSale> AddAsync(ProductSale sale, CancellationToken ct = default);
    Task UpdateAsync(ProductSale sale, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
