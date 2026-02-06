using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KubiczPlace.Finances.Data;
using System.Linq;
using System.Threading.Tasks;

namespace KubiczPlace.Finances.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AnalyticsController : ControllerBase
{
    private readonly FinanceDbContext _context;

    public AnalyticsController(FinanceDbContext context)
    {
        _context = context;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int? workerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _context.ServiceRecords
            .Include(r => r.Worker)
            .Include(r => r.Service)
            .AsQueryable();

        if (workerId.HasValue)
            q = q.Where(r => r.WorkerId == workerId.Value);
        if (from.HasValue)
            q = q.Where(r => r.DatePerformed.Date >= from.Value.Date);
        if (to.HasValue)
            q = q.Where(r => r.DatePerformed.Date <= to.Value.Date);

        var records = await q.ToListAsync();

        var totalRevenue = records.Sum(r => r.AmountPaid);
        var totalTips = records.Sum(r => r.Tips);
        var totalWorkerShare = records.Sum(r => r.AmountPaid * (r.CommissionPercentageApplied / 100));
        var totalSalonShare = totalRevenue - totalWorkerShare;
        var recordCount = records.Count;

        return Ok(new
        {
            totalRevenue,
            totalTips,
            totalWorkerShare,
            totalSalonShare,
            recordCount
        });
    }

    [HttpGet("daily-earnings")]
    public async Task<IActionResult> GetDailyEarnings([FromQuery] int? workerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _context.ServiceRecords.AsQueryable();

        if (workerId.HasValue)
            q = q.Where(r => r.WorkerId == workerId.Value);
        if (from.HasValue)
            q = q.Where(r => r.DatePerformed.Date >= from.Value.Date);
        if (to.HasValue)
            q = q.Where(r => r.DatePerformed.Date <= to.Value.Date);

        var raw = await q.ToListAsync();

        var data = raw
            .GroupBy(r => r.DatePerformed.Date)
            .Select(g => new
            {
                date = g.Key.ToString("yyyy-MM-dd"),
                revenue = g.Sum(r => r.AmountPaid),
                tips = g.Sum(r => r.Tips)
            })
            .OrderBy(x => x.date)
            .ToList();

        return Ok(data);
    }

    [HttpGet("revenue-by-worker")]
    public async Task<IActionResult> GetRevenueByWorker([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _context.ServiceRecords
            .Include(r => r.Worker)
            .AsQueryable();

        if (from.HasValue)
            q = q.Where(r => r.DatePerformed.Date >= from.Value.Date);
        if (to.HasValue)
            q = q.Where(r => r.DatePerformed.Date <= to.Value.Date);

        var raw = await q.ToListAsync();

        var data = raw
            .GroupBy(r => r.Worker?.Name ?? "Unknown")
            .Select(g => new
            {
                worker = g.Key,
                revenue = g.Sum(r => r.AmountPaid),
                tips = g.Sum(r => r.Tips)
            })
            .OrderByDescending(x => x.revenue)
            .ToList();

        return Ok(data);
    }

    [HttpGet("service-popularity")]
    public async Task<IActionResult> GetServicePopularity([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _context.ServiceRecords
            .Include(r => r.Service)
            .AsQueryable();

        if (from.HasValue)
            q = q.Where(r => r.DatePerformed.Date >= from.Value.Date);
        if (to.HasValue)
            q = q.Where(r => r.DatePerformed.Date <= to.Value.Date);

        var raw = await q.ToListAsync();

        var data = raw
            .GroupBy(r => r.Service?.Name ?? "Unknown")
            .Select(g => new
            {
                service = g.Key,
                count = g.Count(),
                revenue = g.Sum(r => r.AmountPaid)
            })
            .OrderByDescending(x => x.count)
            .ToList();

        return Ok(data);
    }

    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent([FromQuery] int count = 5)
    {
        var raw = await _context.ServiceRecords
            .Include(r => r.Worker)
            .Include(r => r.Service)
            .OrderByDescending(r => r.DatePerformed)
            .ThenByDescending(r => r.Id)
            .Take(count)
            .ToListAsync();

        var data = raw.Select(r => new
        {
            r.Id,
            date = r.DatePerformed.ToString("yyyy-MM-dd"),
            worker = r.Worker?.Name ?? "Unknown",
            service = r.Service?.Name ?? "Unknown",
            r.AmountPaid,
            r.Tips,
            r.ClientName
        }).ToList();

        return Ok(data);
    }
}
