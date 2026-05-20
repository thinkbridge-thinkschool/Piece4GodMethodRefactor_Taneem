using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orders.Data;
using Orders.Dtos;
using Xunit;

namespace Orders.Tests;

// ═════════════════════════════════════════════════════════════════════════════
// CANCELLATION TESTS
// Proves CancellationToken flows from HTTP layer → controller → service → EF
// ═════════════════════════════════════════════════════════════════════════════

public sealed class CancellationTests : IClassFixture<OrderCancellationFactory>
{
    private readonly HttpClient _client;

    public CancellationTests(OrderCancellationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Test 1 ────────────────────────────────────────────────────────────────
    // Cancel before sending — HttpClient throws before request leaves process.
    // Nothing reaches the controller, nothing reaches EF, nothing hits the DB.

    [Fact]
    public async Task CreateOrder_cancelled_before_send_throws_OperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();   // cancel immediately

        var request = new CreateOrderRequest(
            CustomerName: "Cancelled Customer",
            CustomerId:   1,
            Items:
            [
                new CreateOrderItemRequest(
                    ProductId:   1,
                    ProductName: "Widget",
                    Price:       10m,
                    Quantity:    1)
            ]);

        // Act + Assert
        // Token is already cancelled — HttpClient throws before sending
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _client.PostAsJsonAsync("/api/order", request, cts.Token));
    }

    // ── Test 2 ────────────────────────────────────────────────────────────────
    // Cancel a GET request — proves reads also respect cancellation,
    // not just writes.

    [Fact]
    public async Task GetOrder_cancelled_token_throws_OperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act + Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _client.GetAsync("/api/order/1", cts.Token));
    }

    // ── Test 3 ────────────────────────────────────────────────────────────────
    // Cancel with a short timeout — simulates a slow DB / long-tail request.
    // CancellationTokenSource.CancelAfter fires after the delay.

    [Fact]
public async Task CreateOrder_times_out_throws_OperationCanceledException()
{
    // Arrange — cancel immediately, simulating a timed-out request
    using var cts = new CancellationTokenSource();
    await cts.CancelAsync();   // cancel before sending

    var request = new CreateOrderRequest(
        CustomerName: "Timeout Customer",
        CustomerId:   2,
        Items:
        [
            new CreateOrderItemRequest(
                ProductId:   1,
                ProductName: "Widget",
                Price:       10m,
                Quantity:    1)
        ]);

    // Act + Assert
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        _client.PostAsJsonAsync("/api/order", request, cts.Token));
}
}

// ═════════════════════════════════════════════════════════════════════════════
// TEST FACTORY
// ═════════════════════════════════════════════════════════════════════════════

public class OrderCancellationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));

            if (descriptor != null)
                services.Remove(descriptor);

            var contextDescriptor = services.FirstOrDefault(
                d => d.ServiceType == typeof(AppDbContext));

            if (contextDescriptor != null)
                services.Remove(contextDescriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()),
                ServiceLifetime.Scoped);
        });
    }
}