namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Models;

public interface IInvoiceProcessor
{
    Task ProcessAsync(Invoice invoice, CancellationToken ct = default);
}
