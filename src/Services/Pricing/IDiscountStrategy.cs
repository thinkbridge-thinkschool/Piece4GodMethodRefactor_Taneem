using Orders.Models;

namespace Orders.Services.Pricing;

/// <summary>
/// Each discount rule implements this.
/// Return 0 if the rule doesn't apply to this order.
/// </summary>
public interface IDiscountStrategy
{
    decimal GetDiscount(Order order);
}