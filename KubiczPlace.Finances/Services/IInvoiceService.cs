namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Models;

public interface IInvoiceService
{
    Task<IReadOnlyList<Invoice>> GetAsync(CancellationToken ct = default);
    Task UploadAsync(Stream pdf, string fileName, CancellationToken ct = default);
    Task ReprocessAsync(int id, CancellationToken ct = default);
}
