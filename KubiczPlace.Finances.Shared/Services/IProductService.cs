using KubiczPlace.Finances.Shared.Models;

namespace KubiczPlace.Finances.Shared.Services;

public interface IProductService
{
    Task<IReadOnlyList<Product>> GetAsync(CancellationToken ct = default);
    Task<Product> AddAsync(Product product, CancellationToken ct = default);
    Task UpdateAsync(Product product, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
