using Microsoft.Extensions.Logging;
using Orders.Dtos;
using Orders.Exceptions;
using Orders.Models;
using Orders.Repositories;

namespace Orders.Services;

public interface IOrderService
{
    Task<PagedResponse<OrderSummaryResponse>> GetAllAsync(
        int page, int pageSize, CancellationToken ct);

    Task<OrderDetailResponse> GetByIdAsync(int id, CancellationToken ct);

    Task<CreateOrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct);

    Task<UpdateOrderResponse> UpdateStatusAsync(
        int id, UpdateOrderRequest request, CancellationToken ct);

    Task<DeleteOrderResponse> DeleteAsync(int id, CancellationToken ct);

    Task<OrderReportResponse> GetReportAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class OrderService : IOrderService
{
    // Threshold above which the full 10 % loyalty discount applies.
    private const int PremiumCustomerThreshold  = 1000;
    // Threshold above which a reduced 5 % loyalty discount applies.
    private const int StandardCustomerThreshold = 500;
    // Bulk discount thresholds (applied to subtotal before loyalty discount).
    private const decimal BulkTierOneThreshold  = 500m;
    private const decimal BulkTierTwoThreshold  = 200m;
    // Maximum open orders allowed per customer.
    private const int MaxOrdersPerCustomer       = 50;
    // Full refund window in days; beyond this only a partial refund is issued.
    private const int FullRefundWindowDays       = 7;

    private readonly IOrderRepository _repo;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IOrderRepository repo, ILogger<OrderService> logger)
    {
        _repo   = repo;
        _logger = logger;
    }

    // ── Query operations ──────────────────────────────────────────────────────

    public async Task<PagedResponse<OrderSummaryResponse>> GetAllAsync(
        int page, int pageSize, CancellationToken ct)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (orders, total) = await _repo.GetPagedAsync(page, pageSize, ct);

        var dtos = orders.Select(ToSummaryResponse).ToList();
        return new PagedResponse<OrderSummaryResponse>(dtos, total, page, pageSize);
    }

    public async Task<OrderDetailResponse> GetByIdAsync(int id, CancellationToken ct)
    {
        var order = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new OrderNotFoundException(id);

        var (discountRate, discountedTotal) = CalculateLoyaltyDiscount(
            order.CustomerId, order.TotalAmount);

        await _repo.AddAuditLogAsync(new AuditLog
        {
            Action    = "GET_ORDER",
            OrderId   = order.Id,
            Timestamp = DateTimeOffset.UtcNow,
            Detail    = $"Fetched order {order.Id} for customer {order.CustomerId}"
        }, ct);

        return ToDetailResponse(order, discountRate, discountedTotal);
    }

    // ── Command operations ────────────────────────────────────────────────────

    public async Task<CreateOrderResponse> CreateAsync(
        CreateOrderRequest request, CancellationToken ct)
    {
        // Business rule: cap open orders per customer.
        var existingCount = await _repo.CountByCustomerAsync(request.CustomerId, ct);
        if (existingCount >= MaxOrdersPerCustomer)
            throw new OrderBusinessRuleException(
                $"Customer {request.CustomerId} already has {existingCount} orders " +
                $"(maximum is {MaxOrdersPerCustomer}).");

        var subtotal = request.Items.Sum(i => i.Price * i.Quantity);
        var total    = ApplyBulkDiscount(subtotal);

        var order = new Order
        {
            CustomerId   = request.CustomerId,
            CustomerName = request.CustomerName,
            TotalAmount  = total,
            Status       = OrderStatus.Pending,
            CreatedAt    = DateTimeOffset.UtcNow,
            UpdatedAt    = DateTimeOffset.UtcNow,
            Items        = request.Items.Select(i => new OrderItem
            {
                ProductId   = i.ProductId,
                ProductName = i.ProductName,
                Price       = i.Price,
                Quantity    = i.Quantity
            }).ToList()
        };

        // Single SaveChanges call persists order + all items in one round-trip.
        var newId = await _repo.AddAsync(order, ct);

        _logger.LogInformation(
            "Order {OrderId} created for customer {CustomerId} with total {Total:C}",
            newId, request.CustomerId, total);

        return new CreateOrderResponse(newId, total, OrderStatus.Pending);
    }

    public async Task<UpdateOrderResponse> UpdateStatusAsync(
        int id, UpdateOrderRequest request, CancellationToken ct)
    {
        // Validate status BEFORE loading or touching any entity.
        if (!OrderStatus.IsValid(request.Status))
            throw new OrderBusinessRuleException(
                $"'{request.Status}' is not a valid order status.");

        var order = await _repo.GetByIdAsync(id, ct)
            ?? throw new OrderNotFoundException(id);

        var previousStatus = order.Status;
        order.Status    = request.Status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Status == OrderStatus.Cancelled)
            await IssueRefundAsync(order, ct);

        await _repo.UpdateAsync(order, ct);

        _logger.LogInformation(
            "Order {OrderId} status changed from {Previous} to {New}",
            id, previousStatus, request.Status);

        return new UpdateOrderResponse(id, order.Status);
    }

    public async Task<DeleteOrderResponse> DeleteAsync(int id, CancellationToken ct)
    {
        var order = await _repo.GetByIdWithItemsAsync(id, ct)
            ?? throw new OrderNotFoundException(id);

        if (order.Status == OrderStatus.Delivered)
            throw new OrderBusinessRuleException(
                $"Order {id} cannot be deleted because it has already been delivered.");

        var customerId = order.CustomerId; // read before Remove

        // Cascade delete on the FK removes child items in the same SaveChanges call.
        await _repo.DeleteAsync(order, ct);

        await _repo.AddAuditLogAsync(new AuditLog
        {
            Action    = "DELETE_ORDER",
            OrderId   = id,
            Timestamp = DateTimeOffset.UtcNow,
            Detail    = $"Deleted order {id} belonging to customer {customerId}"
        }, ct);

        _logger.LogInformation("Order {OrderId} deleted", id);

        return new DeleteOrderResponse(id, "Order deleted successfully.");
    }

    public async Task<OrderReportResponse> GetReportAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var orders = await _repo.GetByDateRangeAsync(from, to, ct);

        // Push aggregation to LINQ-to-Objects; the repository already filtered on the DB.
        var byStatus = orders
            .GroupBy(o => o.Status)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(o => o.TotalAmount));

        var topCustomers = orders
            .GroupBy(o => o.CustomerId)
            .Select(g => new TopCustomerEntry(
                g.Key,
                g.Sum(o => o.TotalAmount),
                g.Count()))
            .OrderByDescending(x => x.TotalSpent)
            .Take(10)
            .ToList();

        // Correct average: divide by Count, not Count - 1.
        var avg = orders.Count > 0
            ? orders.Average(o => o.TotalAmount)
            : 0m;

        return new OrderReportResponse(
            from,
            to,
            orders.Count,
            avg,
            byStatus,
            topCustomers,
            DateTimeOffset.UtcNow);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static decimal ApplyBulkDiscount(decimal subtotal) =>
        subtotal switch
        {
            > BulkTierOneThreshold => subtotal * 0.90m,
            > BulkTierTwoThreshold => subtotal * 0.95m,
            _                      => subtotal
        };

    private static (decimal DiscountRate, decimal DiscountedTotal)
        CalculateLoyaltyDiscount(int customerId, decimal total)
    {
        var rate = customerId switch
        {
            > PremiumCustomerThreshold  => 0.10m,
            > StandardCustomerThreshold => 0.05m,
            _                           => 0m
        };
        return (rate, total - total * rate);
    }

    private async Task IssueRefundAsync(Order order, CancellationToken ct)
    {
        var daysSinceCreation = (DateTimeOffset.UtcNow - order.CreatedAt).TotalDays;
        var refundAmount = daysSinceCreation > FullRefundWindowDays
            ? order.TotalAmount * 0.80m
            : order.TotalAmount;

        var refund = new Refund
        {
            OrderId    = order.Id,
            CustomerId = order.CustomerId,
            Amount     = refundAmount,
            IssuedAt   = DateTimeOffset.UtcNow
        };

        await _repo.AddRefundAsync(refund, ct);

        _logger.LogInformation(
            "Refund of {Amount:C} issued for order {OrderId} (full window: {Full})",
            refundAmount, order.Id, daysSinceCreation <= FullRefundWindowDays);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static OrderSummaryResponse ToSummaryResponse(Order o) => new(
        o.Id, o.CustomerId, o.CustomerName, o.Status,
        o.TotalAmount, o.CreatedAt, o.UpdatedAt);

    private static OrderDetailResponse ToDetailResponse(
        Order o, decimal discountRate, decimal discountedTotal) => new(
        o.Id,
        o.CustomerId,
        o.CustomerName,
        o.Status,
        o.TotalAmount,
        discountedTotal,
        discountRate,
        o.CreatedAt,
        o.UpdatedAt,
        o.Items.Select(i => new OrderItemResponse(
            i.ProductId, i.ProductName, i.Price, i.Quantity, i.Price * i.Quantity
        )).ToList());
}
