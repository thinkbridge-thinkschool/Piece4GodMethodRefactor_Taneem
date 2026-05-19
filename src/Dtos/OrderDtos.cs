using System.ComponentModel.DataAnnotations;

namespace Orders.Dtos;

// ── Requests ──────────────────────────────────────────────────────────────────

public sealed record CreateOrderRequest(
    [Required, StringLength(200, MinimumLength = 1)]
    string CustomerName,

    [Range(1, int.MaxValue, ErrorMessage = "CustomerId must be a positive integer.")]
    int CustomerId,

    [Required, MinLength(1, ErrorMessage = "At least one item is required.")]
    IReadOnlyList<CreateOrderItemRequest> Items
);

public sealed record CreateOrderItemRequest(
    [Range(1, int.MaxValue)] int ProductId,
    [Required, StringLength(200, MinimumLength = 1)] string ProductName,
    [Range(0.01, 1_000_000)] decimal Price,
    [Range(1, 10_000)] int Quantity
);

public sealed record UpdateOrderRequest(
    [Required] string Status
);

// ── Responses ─────────────────────────────────────────────────────────────────

public sealed record OrderSummaryResponse(
    int              Id,
    int              CustomerId,
    string           CustomerName,
    string           Status,
    decimal          TotalAmount,
    DateTimeOffset   CreatedAt,
    DateTimeOffset   UpdatedAt
);

public sealed record OrderDetailResponse(
    int              Id,
    int              CustomerId,
    string           CustomerName,
    string           Status,
    decimal          OriginalTotal,
    decimal          DiscountedTotal,
    decimal          DiscountRate,
    DateTimeOffset   CreatedAt,
    DateTimeOffset   UpdatedAt,
    IReadOnlyList<OrderItemResponse> Items
);

public sealed record OrderItemResponse(
    int     ProductId,
    string  ProductName,
    decimal Price,
    int     Quantity,
    decimal LineTotal
);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int              TotalCount,
    int              Page,
    int              PageSize
);

public sealed record CreateOrderResponse(
    int     OrderId,
    decimal Total,
    string  Status
);

public sealed record UpdateOrderResponse(
    int    OrderId,
    string NewStatus
);

public sealed record DeleteOrderResponse(
    int    DeletedOrderId,
    string Message
);

public sealed record OrderReportResponse(
    DateTimeOffset                    From,
    DateTimeOffset                    To,
    int                               TotalOrders,
    decimal                           AverageOrderValue,
    IReadOnlyDictionary<string, decimal> TotalByStatus,
    IReadOnlyList<TopCustomerEntry>   TopCustomers,
    DateTimeOffset                    GeneratedAt
);

public sealed record TopCustomerEntry(
    int     CustomerId,
    decimal TotalSpent,
    int     OrderCount
);
