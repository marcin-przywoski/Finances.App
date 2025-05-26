namespace KubiczPlace.Finances.Shared.Services;

using KubiczPlace.Finances.Shared.Models;

public interface IWorkerService
{
    Task<IReadOnlyList<Worker>> GetAsync(CancellationToken ct = default);
    Task<Worker> AddAsync(Worker worker, CancellationToken ct = default);
    Task UpdateAsync(Worker worker, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
