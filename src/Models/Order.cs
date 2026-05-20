namespace Orders.Models;

public sealed class Order
{
    public int    Id           { get; set; }
    public int    CustomerId   { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }          // decimal — not double
    public string  Status      { get; set; } = OrderStatus.Pending;

    public CustomerTier CustomerTier { get; set; } = CustomerTier.None;
    public DateTimeOffset CreatedAt { get; set; }     // UTC-aware
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

public sealed class OrderItem
{
    public int     Id          { get; set; }
    public int     OrderId     { get; set; }
    public int     ProductId   { get; set; }
    public string  ProductName { get; set; } = string.Empty;
    public decimal Price       { get; set; }
    public int     Quantity    { get; set; }

    public Order Order { get; set; } = null!;
}

public sealed class Refund
{
    public int            Id         { get; set; }
    public int            OrderId    { get; set; }
    public int            CustomerId { get; set; }
    public decimal        Amount     { get; set; }
    public DateTimeOffset IssuedAt   { get; set; }
}

public sealed class AuditLog
{
    public int            Id        { get; set; }
    public string         Action    { get; set; } = string.Empty;
    public int            OrderId   { get; set; }
    public string         Detail    { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>Valid order status values as constants — no magic strings scattered through the code.</summary>
public static class OrderStatus
{
    public const string Pending    = "Pending";
    public const string Processing = "Processing";
    public const string Shipped    = "Shipped";
    public const string Delivered  = "Delivered";
    public const string Cancelled  = "Cancelled";

    private static readonly HashSet<string> All =
        [Pending, Processing, Shipped, Delivered, Cancelled];

    public static bool IsValid(string status) => All.Contains(status);
}

public enum CustomerTier { None, Standard, Premium }
