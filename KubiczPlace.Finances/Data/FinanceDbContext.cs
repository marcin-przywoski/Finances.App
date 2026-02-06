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
    public DbSet<Product> Products { get; set; }

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

        modelBuilder.Entity<ServiceRecord>()
            .Property(sr => sr.Tips)
            .HasColumnType("decimal(18, 2)");

        modelBuilder.Entity<ServiceRecord>()
            .Ignore(sr => sr.WorkerShare);

        modelBuilder.Entity<ServiceRecord>()
            .Ignore(sr => sr.SalonShare);
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

        modelBuilder.Entity<Product>()
            .Property(p => p.Price)
            .HasColumnType("decimal(18, 2)");

        // Seed data
        modelBuilder.Entity<Worker>().HasData(
            new Worker { Id = 1, Name = "Jan Kowalski", DefaultCommissionPercentage = 50 },
            new Worker { Id = 2, Name = "Anna Nowak", DefaultCommissionPercentage = 45 },
            new Worker { Id = 3, Name = "Piotr Wiśniewski", DefaultCommissionPercentage = 55 }
        );

        modelBuilder.Entity<Service>().HasData(
            new Service { Id = 1, Name = "Strzyżenie męskie", BasePrice = 50 },
            new Service { Id = 2, Name = "Strzyżenie damskie", BasePrice = 80 },
            new Service { Id = 3, Name = "Broda", BasePrice = 30 },
            new Service { Id = 4, Name = "Koloryzacja", BasePrice = 150 },
            new Service { Id = 5, Name = "Strzyżenie + Broda", BasePrice = 70 }
        );
    }
}
