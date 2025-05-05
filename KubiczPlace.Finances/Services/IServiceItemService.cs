namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Models;

public interface IServiceItemService
{
    Task<IReadOnlyList<ServiceItem>> GetAsync(CancellationToken ct = default);
    Task<ServiceItem> AddAsync(ServiceItem item, CancellationToken ct = default);
    Task UpdateAsync(ServiceItem item, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
