// OrderApi.Tests — four tests that expose specific bugs in the original code
// and verify correct behaviour in the refactored version.
//
// Run with:  dotnet test
//
// Each test has a "WHY THIS FAILS ON THE ORIGINAL" comment that identifies
// the exact line/smell in OrderController.cs that caused the failure.

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
using Xunit;

namespace OrderApi.Tests;

// ══════════════════════════════════════════════════════════════════════════════
// UNIT TEST 1 — Discount calculator: correct rates for each customer tier
//
// WHY THIS FAILS ON THE ORIGINAL:
//   The loyalty discount was computed inline in GetOrderById with bare `double`
//   arithmetic and magic number thresholds. There was no way to unit-test it
//   without a real DbContext, because business logic and data access were fused.
//   The test here proves the isolated, decimal-based implementation is correct.
// ══════════════════════════════════════════════════════════════════════════════
public sealed class DiscountCalculationTests
{
    private readonly IOrderService _sut;

    public DiscountCalculationTests()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();
        _sut = new OrderService(repoMock.Object, logger.Object);

        // Wire GetByIdWithItemsAsync for each customer-tier scenario.
        repoMock
            .Setup(r => r.GetByIdWithItemsAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOrder(customerId: 501,  total: 100m)); // 5 % tier

        repoMock
            .Setup(r => r.GetByIdWithItemsAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOrder(customerId: 1001, total: 100m)); // 10 % tier

        repoMock
            .Setup(r => r.GetByIdWithItemsAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeOrder(customerId: 100,  total: 100m)); // no discount

        repoMock
            .Setup(r => r.AddAuditLogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Standard_customer_receives_5_percent_discount()
    {
        var result = await _sut.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal(0.05m,  result.DiscountRate);
        Assert.Equal(95.00m, result.DiscountedTotal);
    }

    [Fact]
    public async Task Premium_customer_receives_10_percent_discount()
    {
        var result = await _sut.GetByIdAsync(2, CancellationToken.None);

        Assert.Equal(0.10m,  result.DiscountRate);
        Assert.Equal(90.00m, result.DiscountedTotal);
    }

    [Fact]
    public async Task Regular_customer_receives_no_discount()
    {
        var result = await _sut.GetByIdAsync(3, CancellationToken.None);

        Assert.Equal(0m,     result.DiscountRate);
        Assert.Equal(100m,   result.DiscountedTotal);
    }

    private static Order MakeOrder(int customerId, decimal total) => new()
    {
        Id           = 1,
        CustomerId   = customerId,
        CustomerName = "Test Customer",
        TotalAmount  = total,
        Status       = OrderStatus.Pending,
        CreatedAt    = DateTimeOffset.UtcNow,
        UpdatedAt    = DateTimeOffset.UtcNow,
        Items        = []
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// UNIT TEST 2 — Report average: divide by Count, not Count − 1
//
// WHY THIS FAILS ON THE ORIGINAL:
//   GetOrderReport (line 406) computed:
//       avgOrderValue = orders.Sum(...) / (orders.Count - 1)
//   For a single-order result set this divides by zero (swallowed by empty
//   catch, returns 0). For two orders it inflates the average by 2×.
//   This test pins the correct behaviour: Sum / Count.
// ══════════════════════════════════════════════════════════════════════════════
public sealed class ReportAverageTests
{
    [Fact]
    public async Task Average_order_value_divides_by_count_not_count_minus_one()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();
        var sut      = new OrderService(repoMock.Object, logger.Object);

        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to   = DateTimeOffset.UtcNow;

        // Two orders: totals 100 and 200 → correct average = 150, not 300 (÷1).
        repoMock
            .Setup(r => r.GetByDateRangeAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                MakeOrder(total: 100m),
                MakeOrder(total: 200m)
            ]);

        var result = await sut.GetReportAsync(from, to, CancellationToken.None);

        Assert.Equal(150m, result.AverageOrderValue);
    }

    [Fact]
    public async Task Average_is_zero_when_no_orders_exist_in_range()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();
        var sut      = new OrderService(repoMock.Object, logger.Object);

        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to   = DateTimeOffset.UtcNow;

        repoMock
            .Setup(r => r.GetByDateRangeAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.GetReportAsync(from, to, CancellationToken.None);

        // Original: divides by -1 (0 - 1), swallowed, returns 0.
        // Refactored: guard on Count > 0, returns 0m cleanly.
        Assert.Equal(0m, result.AverageOrderValue);
    }

    private static Order MakeOrder(decimal total) => new()
    {
        Id          = 1,
        CustomerId  = 1,
        TotalAmount = total,
        Status      = OrderStatus.Pending,
        CreatedAt   = DateTimeOffset.UtcNow,
        UpdatedAt   = DateTimeOffset.UtcNow,
        Items       = []
    };
}

// ══════════════════════════════════════════════════════════════════════════════
// UNIT TEST 3 — Status validation happens before entity mutation
//
// WHY THIS FAILS ON THE ORIGINAL:
//   UpdateOrder (lines 265, 293–300) set order.Status on the tracked entity
//   *before* checking whether the status value was valid. A bad status was
//   written to the in-memory entity, the Refund row was inserted, and then
//   the guard triggered — but the Refund was already committed.
//   The refactored service validates status as the very first step.
// ══════════════════════════════════════════════════════════════════════════════
public sealed class StatusValidationTests
{
    [Fact]
    public async Task Invalid_status_throws_business_rule_exception_before_any_db_call()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();
        var sut      = new OrderService(repoMock.Object, logger.Object);

        var request = new UpdateOrderRequest("INVALID_STATUS");

        var ex = await Assert.ThrowsAsync<OrderBusinessRuleException>(
            () => sut.UpdateStatusAsync(42, request, CancellationToken.None));

        Assert.Contains("INVALID_STATUS", ex.Message);

        // The repository must never have been called — no entity was loaded or mutated.
        repoMock.Verify(
            r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        repoMock.Verify(
            r => r.AddRefundAsync(It.IsAny<Refund>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Valid_status_proceeds_to_load_entity()
    {
        var repoMock = new Mock<IOrderRepository>();
        var logger   = new Mock<ILogger<OrderService>>();
        var sut      = new OrderService(repoMock.Object, logger.Object);

        repoMock
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Order
            {
                Id           = 1,
                CustomerId   = 1,
                CustomerName = "Alice",
                Status       = OrderStatus.Pending,
                TotalAmount  = 100m,
                CreatedAt    = DateTimeOffset.UtcNow,
                UpdatedAt    = DateTimeOffset.UtcNow
            });

        repoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await sut.UpdateStatusAsync(
            1, new UpdateOrderRequest(OrderStatus.Processing), CancellationToken.None);

        Assert.Equal(OrderStatus.Processing, result.NewStatus);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// INTEGRATION TEST — WebApplicationFactory end-to-end
//
// WHY THE ORIGINAL FAILS THESE SCENARIOS:
//
//  CreateOrder_returns_201_with_typed_body:
//    The original accepted `dynamic` payload, could not be model-validated,
//    returned `object`, and always returned HTTP 200 even on success. The
//    new endpoint returns 201 Created with a typed CreateOrderResponse body.
//
//  GetOrderById_returns_404_for_missing_order:
//    The original returned HTTP 200 with a null body for missing orders
//    (order == null fell through after the empty catch, returned null!).
//    The refactored endpoint returns a proper 404 ProblemDetails.
//
//  CreateOrder_returns_422_when_customer_order_limit_exceeded:
//    The original returned HTTP 200 with { error: "Customer has too many orders" }
//    The refactored endpoint returns 422 UnprocessableEntity.
// ══════════════════════════════════════════════════════════════════════════════
public sealed class OrderIntegrationTests : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client;

    public OrderIntegrationTests(OrderApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateOrder_returns_201_with_typed_body()
    {
        var request = new CreateOrderRequest(
            CustomerName : "Integration Tester",
            CustomerId   : 42,
            Items        :
            [
                new CreateOrderItemRequest(
                    ProductId   : 1,
                    ProductName : "Widget",
                    Price       : 10.00m,
                    Quantity    : 3)
            ]);

        var response = await _client.PostAsJsonAsync("/api/order", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        Assert.NotNull(body);
        Assert.True(body.OrderId > 0);
        Assert.Equal(OrderStatus.Pending, body.Status);
        // Bulk discount does not apply (total = 30): price × qty = 30m
        Assert.Equal(30.00m, body.Total);
    }

    [Fact]
    public async Task GetOrderById_returns_404_for_missing_order()
    {
        // 99999 does not exist in the in-memory test database.
        var response = await _client.GetAsync("/api/order/99999");

        // Original: returned 200 with null body.
        // Refactored: must return 404.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_returns_400_for_invalid_request_body()
    {
        // Empty Items list violates the [MinLength(1)] annotation.
        var request = new CreateOrderRequest(
            CustomerName : "Bad Request User",
            CustomerId   : 1,
            Items        : []); // no items

        var response = await _client.PostAsJsonAsync("/api/order", request);

        // Original: fell through into the action, hit the null-deref on items.Count,
        // threw NullReferenceException inside an empty catch, returned 200 null.
        // Refactored: [ApiController] model validation returns 400 before the action runs.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteOrder_returns_422_when_order_is_delivered()
    {
        // Step 1 — create an order so we have a valid ID.
        var createRequest = new CreateOrderRequest(
            "Delivered Customer", 7,
            [new CreateOrderItemRequest(1, "Gadget", 50m, 1)]);

        var createResponse = await _client.PostAsJsonAsync("/api/order", createRequest);
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<CreateOrderResponse>();
        Assert.NotNull(created);

        // Step 2 — advance status to Delivered.
        await _client.PutAsJsonAsync(
            $"/api/order/{created.OrderId}",
            new UpdateOrderRequest(OrderStatus.Delivered));

        // Step 3 — attempt to delete the delivered order.
        var deleteResponse = await _client.DeleteAsync($"/api/order/{created.OrderId}");

        // Original: returned 200 { error: "Cannot delete a delivered order" }.
        // Refactored: returns 422 UnprocessableEntity.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deleteResponse.StatusCode);
    }
}

// ── Test host factory ─────────────────────────────────────────────────────────

/// <summary>
/// Replaces the real SQLite database with an isolated in-memory EF Core
/// database for each test class instantiation. No file I/O; no shared state.
/// </summary>
public sealed class OrderApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            // Replace with an in-memory database, unique per factory instance.
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseInMemoryDatabase($"TestDb-{Guid.NewGuid()}"));
        });

        builder.UseEnvironment("Development");
    }
}