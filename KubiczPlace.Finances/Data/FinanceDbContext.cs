using KubiczPlace.Finances.Shared;
using Microsoft.EntityFrameworkCore;

namespace KubiczPlace.Finances.Data;

public class FinanceDbContext : DbContext
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
    {
    }

    public DbSet<Worker> Workers { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<ServiceRecord> ServiceRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure decimal precision for currency/percentage properties
        modelBuilder.Entity<Worker>()
            .Property(w => w.DefaultCommissionPercentage)
            .HasColumnType("decimal(5, 2)"); // Example: 999.99

        modelBuilder.Entity<Service>()
            .Property(s => s.BasePrice)
            .HasColumnType("decimal(18, 2)"); // Standard currency precision

        modelBuilder.Entity<ServiceRecord>()
            .Property(sr => sr.AmountPaid)
            .HasColumnType("decimal(18, 2)");

        modelBuilder.Entity<ServiceRecord>()
            .Property(sr => sr.CommissionPercentageApplied)
            .HasColumnType("decimal(5, 2)");
            
        // You can add more configurations here, like relationships, constraints, etc.
        // For example, setting up relationships explicitly (though EF Core can infer them by convention):
        modelBuilder.Entity<ServiceRecord>()
            .HasOne(sr => sr.Worker)
            .WithMany() // Assuming a Worker can have many ServiceRecords
            .HasForeignKey(sr => sr.WorkerId)
            .OnDelete(DeleteBehavior.Restrict); // Or Cascade, SetNull, depending on your rules

        modelBuilder.Entity<ServiceRecord>()
            .HasOne(sr => sr.Service)
            .WithMany() // Assuming a Service can be part of many ServiceRecords
            .HasForeignKey(sr => sr.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
