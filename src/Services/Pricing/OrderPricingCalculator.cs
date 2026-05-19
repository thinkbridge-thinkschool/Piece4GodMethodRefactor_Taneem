using Orders.Models;

namespace Orders.Services.Pricing;

public sealed class OrderPricingCalculator
{
    private readonly IEnumerable<IDiscountStrategy> _strategies;

    public OrderPricingCalculator(IEnumerable<IDiscountStrategy> strategies)
    {
        _strategies = strategies;
    }

    public (decimal DiscountRate, decimal DiscountedTotal) GetDiscountInfo(Order order)
{
    var rate = _strategies
        .Select(s => s.GetDiscount(order))
        .FirstOrDefault(d => d > 0m);

    return (rate, Math.Round(order.TotalAmount * (1 - rate), 2, MidpointRounding.AwayFromZero));
}
    public decimal Calculate(Order order)
    {
        var subtotal = order.Items.Sum(i => i.Price * i.Quantity);

        var discount = _strategies
            .Select(s => s.GetDiscount(order))
            .FirstOrDefault(d => d > 0m);

        return Math.Round(subtotal * (1 - discount), 2, MidpointRounding.AwayFromZero);
    }
}