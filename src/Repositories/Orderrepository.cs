using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orders.Data;
using Orders.Models;

namespace Orders.Repositories;

/// <summary>
/// All EF Core queries live here. No business logic; no HTTP concerns.
/// Every method is async and accepts a CancellationToken.
/// </summary>
public interface IOrderRepository
{
    Task<(IReadOnlyList<Order> Orders, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct);

    Task<Order?> GetByIdAsync(int id, CancellationToken ct);

    Task<Order?> GetByIdWithItemsAsync(int id, CancellationToken ct);

    Task<int> CountByCustomerAsync(int customerId, CancellationToken ct);

    Task<List<Order>> GetByDateRangeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<int> AddAsync(Order order, CancellationToken ct);

    Task UpdateAsync(Order order, CancellationToken ct);

    Task DeleteAsync(Order order, CancellationToken ct);

    Task AddRefundAsync(Refund refund, CancellationToken ct);

    Task AddAuditLogAsync(AuditLog entry, CancellationToken ct);
}

public sealed class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _db;
    private readonly ILogger<OrderRepository> _logger;

    public OrderRepository(AppDbContext db, ILogger<OrderRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(IReadOnlyList<Order> Orders, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
        try
        {
            var totalCount = await _db.Orders.CountAsync(ct);

            // SQLite does not support ORDER BY on DateTimeOffset directly,
            // so fetch first and order in memory.
            var orders = (await _db.Orders
                    .AsNoTracking()
                    .ToListAsync(ct))
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return (orders, totalCount);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error fetching paged orders (page={Page})", page);
            throw;
        }
    }

    public async Task<Order?> GetByIdAsync(int id, CancellationToken ct)
    {
        try
        {
            return await _db.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error fetching order {Id}", id);
            throw;
        }
    }

    public async Task<Order?> GetByIdWithItemsAsync(int id, CancellationToken ct)
    {
        try
        {
            return await _db.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error fetching order {Id} with items", id);
            throw;
        }
    }

    public async Task<int> CountByCustomerAsync(int customerId, CancellationToken ct)
    {
        try
        {
            return await _db.Orders
                .CountAsync(o => o.CustomerId == customerId, ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error counting orders for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<List<Order>> GetByDateRangeAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var orders = await _db.Orders
            .AsNoTracking()
            .ToListAsync(ct);

            return orders
            .Where(o => o.CreatedAt >= from && o.CreatedAt <= to)
            .OrderBy(o => o.CreatedAt)
            .ToList();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Database error fetching orders for date range {From}–{To}",
                from, to);

            throw;
        }
    }

    public async Task<int> AddAsync(Order order, CancellationToken ct)
    {
        try
        {
            _db.Orders.Add(order);

            await _db.SaveChangesAsync(ct);

            return order.Id;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Database error persisting new order for customer {CustomerId}",
                order.CustomerId);

            throw;
        }
    }

    public async Task UpdateAsync(Order order, CancellationToken ct)
    {
        try
        {
            _db.Orders.Update(order);

            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex,
                "Concurrency conflict updating order {Id}",
                order.Id);

            throw;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Database error updating order {Id}",
                order.Id);

            throw;
        }
    }

    public async Task DeleteAsync(Order order, CancellationToken ct)
    {
        try
        {
            _db.Orders.Remove(order);

            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Database error deleting order {Id}",
                order.Id);

            throw;
        }
    }

    public async Task AddRefundAsync(Refund refund, CancellationToken ct)
    {
        try
        {
            _db.Refunds.Add(refund);

            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Database error persisting refund for order {OrderId}",
                refund.OrderId);

            throw;
        }
    }

    public async Task AddAuditLogAsync(AuditLog entry, CancellationToken ct)
    {
        try
        {
            _db.AuditLogs.Add(entry);

            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex,
                "Failed to write audit log for action {Action} on order {OrderId}",
                entry.Action,
                entry.OrderId);
        }
    }
}