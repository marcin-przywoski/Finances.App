namespace KubiczPlace.Finances.Shared.Services;

using KubiczPlace.Finances.Shared.Models;

public interface IInvoiceProcessor
{
    Task ProcessAsync(Invoice invoice, CancellationToken ct = default);
}
