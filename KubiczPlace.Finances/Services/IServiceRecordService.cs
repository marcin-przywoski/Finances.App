namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Models;

public interface IServiceRecordService
{
    Task<IReadOnlyList<ServiceRecord>> GetAsync(CancellationToken ct = default);
    Task<ServiceRecord> AddAsync(ServiceRecord rec, CancellationToken ct = default);
    Task UpdateAsync(ServiceRecord rec, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
