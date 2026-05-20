using Orders.Models;

namespace Orders.Services.Pricing;

// Mirrors the PremiumCustomerThreshold = 1000 constant from OrderService
public sealed class PremiumLoyaltyDiscount : IDiscountStrategy
{
    public decimal GetDiscount(Order order) =>
        order.CustomerId > 1000 ? 0.10m : 0m;
}

// Mirrors the StandardCustomerThreshold = 500 constant from OrderService
public sealed class StandardLoyaltyDiscount : IDiscountStrategy
{
    public decimal GetDiscount(Order order) =>
        order.CustomerId > 500 ? 0.05m : 0m;
}

// Mirrors ApplyBulkDiscount from OrderService
public sealed class BulkOrderDiscount : IDiscountStrategy
{
    public decimal GetDiscount(Order order)
    {
        var subtotal = order.Items.Sum(i => i.Price * i.Quantity);
        return subtotal switch
        {
            > 500m => 0.10m,
            > 200m => 0.05m,
            _      => 0m
        };
    }
}