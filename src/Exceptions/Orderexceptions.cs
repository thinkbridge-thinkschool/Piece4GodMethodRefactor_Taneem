namespace Orders.Exceptions;

/// <summary>Raised when a requested order does not exist.</summary>
public sealed class OrderNotFoundException : Exception
{
    public int OrderId { get; }

    public OrderNotFoundException(int orderId)
        : base($"Order {orderId} was not found.")
    {
        OrderId = orderId;
    }
}

/// <summary>Raised when a business rule prevents the requested operation.</summary>
public sealed class OrderBusinessRuleException : Exception
{
    public OrderBusinessRuleException(string message) : base(message) { }
}
