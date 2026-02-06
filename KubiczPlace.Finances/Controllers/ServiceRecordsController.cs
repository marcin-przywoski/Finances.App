using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KubiczPlace.Finances.Data;
using KubiczPlace.Finances.Shared;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KubiczPlace.Finances.Shared.Models;

namespace KubiczPlace.Finances.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServiceRecordsController : ControllerBase
    {
        private readonly FinanceDbContext _context;

        public ServiceRecordsController(FinanceDbContext context)
        {
            _context = context;
        }

        // GET: api/ServiceRecords
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ServiceRecord>>> GetServiceRecords()
        {
            return await _context.ServiceRecords
                                 .Include(sr => sr.Worker)
                                 .Include(sr => sr.Service)
                                 .ToListAsync();
        }

        // GET: api/ServiceRecords/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ServiceRecord>> GetServiceRecord(int id)
        {
            var serviceRecord = await _context.ServiceRecords
                                              .Include(sr => sr.Worker)
                                              .Include(sr => sr.Service)
                                              .FirstOrDefaultAsync(sr => sr.Id == id);

            if (serviceRecord == null)
            {
                return NotFound();
            }

            return serviceRecord;
        }

        // POST: api/ServiceRecords
        [HttpPost]
        public async Task<ActionResult<ServiceRecord>> PostServiceRecord(ServiceRecord serviceRecord)
        {
            // Ensure related entities are not re-added if they already exist and are part of the payload
            if (serviceRecord.Worker != null && serviceRecord.WorkerId > 0)
                 _context.Entry(serviceRecord.Worker).State = EntityState.Unchanged;
            if (serviceRecord.Service != null && serviceRecord.ServiceId > 0)
                 _context.Entry(serviceRecord.Service).State = EntityState.Unchanged;

            _context.ServiceRecords.Add(serviceRecord);
            await _context.SaveChangesAsync();

            // It's good practice to return the created record, potentially with includes if needed by client
            var createdRecord = await _context.ServiceRecords
                                     .Include(sr => sr.Worker)
                                     .Include(sr => sr.Service)
                                     .FirstOrDefaultAsync(sr => sr.Id == serviceRecord.Id);

            return CreatedAtAction(nameof(GetServiceRecord), new { id = serviceRecord.Id }, createdRecord);
        }

        // PUT: api/ServiceRecords/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutServiceRecord(int id, ServiceRecord serviceRecord)
        {
            if (id != serviceRecord.Id)
            {
                return BadRequest();
            }

            _context.Entry(serviceRecord).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ServiceRecordExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/ServiceRecords/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteServiceRecord(int id)
        {
            var serviceRecord = await _context.ServiceRecords.FindAsync(id);
            if (serviceRecord == null)
            {
                return NotFound();
            }

            _context.ServiceRecords.Remove(serviceRecord);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool ServiceRecordExists(int id)
        {
            return _context.ServiceRecords.Any(e => e.Id == id);
        }
    }
}
