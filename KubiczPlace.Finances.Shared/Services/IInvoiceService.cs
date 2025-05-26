namespace KubiczPlace.Finances.Shared.Services;

using KubiczPlace.Finances.Shared.Models;

public interface IInvoiceService
{
    Task<IReadOnlyList<Invoice>> GetAsync(CancellationToken ct = default);
    Task UploadAsync(Stream pdf, string fileName, CancellationToken ct = default);
    Task ReprocessAsync(int id, CancellationToken ct = default);
}
