using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Orders.Data;
using Orders.Dtos;
using Orders.Exceptions;
using Orders.Models;
using Orders.Repositories;
using Orders.Services;
using Orders.Services.Pricing;
using Xunit;

namespace Orders.Tests;

// ═════════════════════════════════════════════════════════════════════════════
// UNIT TEST 1
// Discount logic
// ═════════════════════════════════════════════════════════════════════════════

public sealed class DiscountCalculationTests
{
    private readonly IOrderService _sut;

    public DiscountCalculationTests()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();

        var strategies = new List<IDiscountStrategy>
        {
            new PremiumLoyaltyDiscount(),
            new StandardLoyaltyDiscount(),
            new BulkOrderDiscount()
        };
        var pricing = new OrderPricingCalculator(strategies);

        _sut = new OrderService(repoMock.Object, logger.Object, pricing);

        repoMock
            .Setup(r => r.GetByIdWithItemsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Order
            {
                Id           = 1,
                CustomerId   = 600,
                CustomerName = "Premium User",
                TotalAmount  = 100m,
                Status       = OrderStatus.Pending,
                CreatedAt    = DateTimeOffset.UtcNow,
                UpdatedAt    = DateTimeOffset.UtcNow,
                Items        = []
            });

        repoMock
            .Setup(r => r.AddAuditLogAsync(
                It.IsAny<AuditLog>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Premium_customer_gets_5_percent_discount()
    {
        var result = await _sut.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal(0.05m, result.DiscountRate);
        Assert.Equal(95m, result.DiscountedTotal);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// UNIT TEST 2
// Average calculation
// ═════════════════════════════════════════════════════════════════════════════

public sealed class ReportAverageTests
{
    [Fact]
    public async Task Average_order_value_divides_by_count()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();

        var strategies = new List<IDiscountStrategy>
        {
            new PremiumLoyaltyDiscount(),
            new StandardLoyaltyDiscount(),
            new BulkOrderDiscount()
        };
        var pricing = new OrderPricingCalculator(strategies);

        var sut = new OrderService(repoMock.Object, logger.Object, pricing);

        var from = DateTimeOffset.UtcNow.AddDays(-5);
        var to   = DateTimeOffset.UtcNow;

        repoMock
            .Setup(r => r.GetByDateRangeAsync(
                from,
                to,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Order
                {
                    Id          = 1,
                    CustomerId  = 1,
                    TotalAmount = 100m,
                    Status      = OrderStatus.Pending,
                    CreatedAt   = DateTimeOffset.UtcNow,
                    UpdatedAt   = DateTimeOffset.UtcNow
                },
                new Order
                {
                    Id          = 2,
                    CustomerId  = 2,
                    TotalAmount = 200m,
                    Status      = OrderStatus.Pending,
                    CreatedAt   = DateTimeOffset.UtcNow,
                    UpdatedAt   = DateTimeOffset.UtcNow
                }
            ]);

        var result = await sut.GetReportAsync(from, to, CancellationToken.None);

        Assert.Equal(150m, result.AverageOrderValue);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// UNIT TEST 3
// Invalid status validation
// ═════════════════════════════════════════════════════════════════════════════

public sealed class StatusValidationTests
{
    [Fact]
    public async Task Invalid_status_throws_exception_before_db_access()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();

        var strategies = new List<IDiscountStrategy>
        {
            new PremiumLoyaltyDiscount(),
            new StandardLoyaltyDiscount(),
            new BulkOrderDiscount()
        };
        var pricing = new OrderPricingCalculator(strategies);

        var sut = new OrderService(repoMock.Object, logger.Object, pricing);

        var request = new UpdateOrderRequest("INVALID");

        await Assert.ThrowsAsync<OrderBusinessRuleException>(() =>
            sut.UpdateStatusAsync(1, request, CancellationToken.None));

        repoMock.Verify(
            r => r.GetByIdAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        repoMock.Verify(
            r => r.AddRefundAsync(
                It.IsAny<Refund>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// INTEGRATION TEST
// WebApplicationFactory
// ═════════════════════════════════════════════════════════════════════════════

public sealed class OrderIntegrationTests
    : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client;

    public OrderIntegrationTests(OrderApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_returns_201_created()
    {
        var request = new CreateOrderRequest(
            CustomerName: "Integration User",
            CustomerId:   10,
            Items:
            [
                new CreateOrderItemRequest(
                    ProductId:   1,
                    ProductName: "Keyboard",
                    Price:       50m,
                    Quantity:    2)
            ]);

        var response = await _client.PostAsJsonAsync("/api/order", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();

        Assert.NotNull(body);
        Assert.True(body.OrderId > 0);
        Assert.Equal(100m, body.Total);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// TEST FACTORY
// ═════════════════════════════════════════════════════════════════════════════

public class OrderApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove existing database registrations
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.RemoveAll(typeof(AppDbContext));

            // Add isolated in-memory database
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(Guid.NewGuid().ToString());
            });

            var sp = services.BuildServiceProvider();

            using var scope = sp.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            db.Database.EnsureCreated();
        });
    }
}