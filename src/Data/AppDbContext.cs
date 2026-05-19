using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orders.Models;

namespace Orders.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order>     Orders     => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Refund>    Refunds    => Set<Refund>();
    public DbSet<AuditLog>  AuditLogs  => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new RefundConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
    }
}

// ── Entity configurations ─────────────────────────────────────────────────────

file sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasKey(o => o.Id);
        b.Property(o => o.CustomerName).IsRequired().HasMaxLength(200);
        b.Property(o => o.Status).IsRequired().HasMaxLength(20);
        b.Property(o => o.TotalAmount).HasPrecision(18, 4);

        b.HasMany(o => o.Items)
         .WithOne(i => i.Order)
         .HasForeignKey(i => i.OrderId)
         .OnDelete(DeleteBehavior.Cascade);   // items go with the order

        b.HasIndex(o => o.CustomerId);
        b.HasIndex(o => o.Status);
        b.HasIndex(o => o.CreatedAt);
    }
}

file sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.HasKey(i => i.Id);
        b.Property(i => i.ProductName).IsRequired().HasMaxLength(200);
        b.Property(i => i.Price).HasPrecision(18, 4);
    }
}

file sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> b)
    {
        b.HasKey(r => r.Id);
        b.Property(r => r.Amount).HasPrecision(18, 4);
        b.HasIndex(r => r.OrderId);
    }
}

file sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasKey(a => a.Id);
        b.Property(a => a.Action).IsRequired().HasMaxLength(50);
        b.Property(a => a.Detail).IsRequired().HasMaxLength(500);
    }
}
