namespace KubiczPlace.Finances.Services;

using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Models;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Server-side implementation that talks to the DbContext directly.
/// </summary>
public class InvoiceServiceDb(ApplicationDbContext db, IInvoiceProcessor processor) : IInvoiceService
{
    public async Task<IReadOnlyList<Invoice>> GetAsync(CancellationToken ct = default)
        => await db.Invoices.AsNoTracking().OrderByDescending(i => i.UploadDate).ToListAsync(ct);

    public async Task UploadAsync(Stream pdf, string fileName, CancellationToken ct = default)
    {
        var bytes = await StreamToBytesAsync(pdf, ct);
        var inv = new Invoice { FileName = fileName, Content = bytes };
        await processor.ProcessAsync(inv, ct);
        db.Invoices.Add(inv);
        await db.SaveChangesAsync(ct);
    }

    public async Task ReprocessAsync(int id, CancellationToken ct = default)
    {
        var inv = await db.Invoices.FindAsync([id], ct);
        if (inv is null) return;
        await processor.ProcessAsync(inv, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<byte[]> StreamToBytesAsync(Stream s, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }
}
