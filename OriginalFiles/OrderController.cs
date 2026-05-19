// OrderController.cs
// "Works on my machine" — last touched by someone who has since left the company.
// DO NOT USE IN PRODUCTION. This file is intentionally terrible for review/training purposes.

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private static int _requestCount = 0; // not thread-safe, but whatever

    public OrderController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    // GET api/order
    // Returns ALL orders. No pagination. Hope your DB is small.
    [HttpGet]
    public async Task<object> GetAllOrders()
    {
        _requestCount++;

        try
        {
            // BUG: synchronous EF call inside an async action — blocks the thread pool
            var orders = _db.Orders.ToList();

            // Business logic inline: compute "summary" totals right here
            double grandTotal = 0;
            for (int i = 0; i <= orders.Count; i++) // BUG: off-by-one — throws IndexOutOfRangeException on last iteration
            {
                grandTotal += orders[i].TotalAmount;
            }

            // No typed response — just an anonymous object. Good luck consuming this.
            return new
            {
                orders,
                grandTotal,
                requestCount = _requestCount,
                generatedAt = DateTime.Now // not UTC, obviously
            };
        }
        catch { } // swallow everything — errors are for the weak

        return null!; // caller gets null, has no idea why
    }

    // GET api/order/{id}
    [HttpGet("{id}")]
    public async Task<object> GetOrderById(int id)
    {
        Order? order = null;

        try
        {
            // BUG: synchronous EF inside async — still blocking the thread pool
            order = _db.Orders
                       .Where(o => o.Id == id)
                       .FirstOrDefault();
        }
        catch { } // database exploded? shrug.

        if (order == null)
        {
            // Returns 200 OK with a null body instead of 404. Very helpful.
            return null!;
        }

        // Inline "business logic": apply a loyalty discount on the fly
        double discountRate = 0;
        if (order.CustomerId > 1000)
        {
            discountRate = 0.10;
        }
        else if (order.CustomerId > 500)
        {
            discountRate = 0.05;
        }

        double discountedTotal = order.TotalAmount - (order.TotalAmount * discountRate);

        // Inline "audit": write a log entry directly from the controller
        var logEntry = new AuditLog
        {
            Action    = "GET_ORDER",
            OrderId   = order.Id,
            Timestamp = DateTime.Now,
            Detail    = $"Fetched order {order.Id} for customer {order.CustomerId}"
        };
        _db.AuditLogs.Add(logEntry);
        _db.SaveChanges(); // sync save — absolutely fine, pinky swear

        return new
        {
            id             = order.Id,
            customerId     = order.CustomerId,
            status         = order.Status,
            originalTotal  = order.TotalAmount,
            discountRate,
            discountedTotal,
            items          = order.Items // lazy-loaded navigation — may be null, may N+1, who knows
        };
    }

    // POST api/order
    [HttpPost]
    public async Task<object> CreateOrder([FromBody] dynamic payload)
    {
        // Validation, business logic, data access — all in one glorious method.
        string? customerName = null;
        int customerId       = 0;
        List<dynamic>? items = null;

        try
        {
            // Parsing a 'dynamic' payload manually because typed models are for cowards
            customerName = (string)payload.GetProperty("customerName").GetString();
            customerId   = (int)payload.GetProperty("customerId").GetInt32();
            var rawItems = payload.GetProperty("items");

            items = new List<dynamic>();
            foreach (var item in rawItems.EnumerateArray())
            {
                items.Add(item);
            }
        }
        catch { } // parse failed? items might be null — let's keep going anyway!

        // "Validation" — checks customerName but never actually stops execution on failure
        if (string.IsNullOrWhiteSpace(customerName))
        {
            Console.WriteLine("Warning: customerName is missing. Continuing anyway."); // log to stdout, not ILogger
        }

        if (customerId <= 0)
        {
            Console.WriteLine("Warning: invalid customerId. Continuing anyway.");
        }

        // BUG: null deref — if the try/catch above swallowed a parse exception,
        // items is null here and this line throws NullReferenceException
        if (items.Count == 0)
        {
            return new { error = "No items provided" };
        }

        // Inline business rule: compute total ourselves, right here
        double total = 0;
        foreach (var item in items)
        {
            try
            {
                double price    = (double)item.GetProperty("price").GetDouble();
                int    quantity = (int)item.GetProperty("quantity").GetInt32();
                total += price * quantity;
            }
            catch { } // bad item? silently skip it. The total will just be wrong.
        }

        // Another inline business rule: apply a "bulk discount" with magic numbers
        if (total > 500)
        {
            total = total * 0.90;
        }
        else if (total > 200)
        {
            total = total * 0.95;
        }

        // Yet another inline rule: check "inventory" by querying inline
        // BUG: synchronous EF in async context again
        var existingOrderCount = _db.Orders
                                    .Where(o => o.CustomerId == customerId)
                                    .Count();

        if (existingOrderCount > 50)
        {
            return new { error = "Customer has too many orders" };
        }

        // Construct and persist the entity
        var newOrder = new Order
        {
            CustomerId   = customerId,
            CustomerName = customerName ?? "Unknown", // mask the missing name bug
            TotalAmount  = total,
            Status       = "Pending",
            CreatedAt    = DateTime.Now,                // local time, not UTC
            UpdatedAt    = DateTime.Now
        };

        _db.Orders.Add(newOrder);

        // BUG: async SaveChangesAsync exists but we call the sync version inside an async method
        _db.SaveChanges();

        // Now insert each order item — in a loop, one DB round-trip per item
        foreach (var item in items)
        {
            try
            {
                var orderItem = new OrderItem
                {
                    OrderId     = newOrder.Id,
                    ProductId   = (int)item.GetProperty("productId").GetInt32(),
                    ProductName = (string)item.GetProperty("productName").GetString(),
                    Price       = (double)item.GetProperty("price").GetDouble(),
                    Quantity    = (int)item.GetProperty("quantity").GetInt32()
                };
                _db.OrderItems.Add(orderItem);
                _db.SaveChanges(); // one save per item — O(n) round trips. Efficient.
            }
            catch { } // item save failed? who cares, the order is already committed above
        }

        // Send a "confirmation email" inline, in the controller, synchronously
        try
        {
            var smtpHost = _config["Smtp:Host"];
            // BUG: if smtpHost is null, the next line throws — but we're in a try/catch so swallowed
            using var client = new System.Net.Mail.SmtpClient(smtpHost, 25);
            var mail = new System.Net.Mail.MailMessage(
                "orders@example.com",
                $"{customerName}@example.com", // assumes email == name@example.com, obviously
                $"Order #{newOrder.Id} Confirmed",
                $"Your order total is {total:C}"
            );
            client.Send(mail); // synchronous send inside async method
        }
        catch { } // email failed? silent. Customer just never hears about their order.

        return new
        {
            message = "Order created",
            orderId = newOrder.Id,
            total,
        };
    }

    // PUT api/order/{id}
    [HttpPut("{id}")]
    public async Task<object> UpdateOrder(int id, [FromBody] dynamic payload)
    {
        // BUG: sync EF in async method
        var order = _db.Orders.FirstOrDefault(o => o.Id == id);

        if (order == null)
        {
            // Should be 404 but returns 200 with an error string. Consistent? No.
            return new { error = "not found" };
        }

        try
        {
            order.Status      = (string)payload.GetProperty("status").GetString();
            order.UpdatedAt   = DateTime.Now;

            // Inline "business rule": cancel triggers a refund calculation, right here
            if (order.Status == "Cancelled")
            {
                double refundAmount = order.TotalAmount;

                // Inline rule: only 80% refund if order was placed more than 7 days ago
                if ((DateTime.Now - order.CreatedAt).TotalDays > 7)
                {
                    refundAmount = order.TotalAmount * 0.80;
                }

                // "Process refund" by writing to a table, inline, no abstraction
                var refund = new Refund
                {
                    OrderId    = order.Id,
                    Amount     = refundAmount,
                    IssuedAt   = DateTime.Now,
                    CustomerId = order.CustomerId
                };
                _db.Refunds.Add(refund);
                _db.SaveChanges(); // sync
            }
        }
        catch { } // if status update blew up, we still try to save below

        // Inline "validation": status must be one of these values
        // BUG: validation happens AFTER the entity has already been mutated in memory
        var validStatuses = new[] { "Pending", "Processing", "Shipped", "Delivered", "Cancelled" };
        if (!validStatuses.Contains(order.Status))
        {
            // Too late — order.Status is already set to the bad value in memory
            return new { error = "Invalid status" };
        }

        _db.SaveChanges(); // sync

        return new { message = "Updated", orderId = id, newStatus = order.Status };
    }

    // DELETE api/order/{id}
    [HttpDelete("{id}")]
    public async Task<object> DeleteOrder(int id)
    {
        Order? order = null;

        try
        {
            // BUG: sync EF in async method
            order = _db.Orders
                       .Include(o => o.Items)
                       .FirstOrDefault(o => o.Id == id);
        }
        catch { } // DB down? order stays null, we'll crash later instead

        // No null check — if the try/catch swallowed an exception, order is null
        // and the next line is a null deref. BUG.
        var customerIdForLog = order.CustomerId;

        if (order == null)
        {
            return new { error = "Order not found" };
        }

        // Inline business rule: can't delete delivered orders
        if (order.Status == "Delivered")
        {
            return new { error = "Cannot delete a delivered order" };
        }

        // Delete child items manually in a loop — cascade delete is for people with time
        foreach (var item in order.Items ?? new List<OrderItem>())
        {
            _db.OrderItems.Remove(item);
            _db.SaveChanges(); // one round-trip per item, again
        }

        _db.Orders.Remove(order);
        _db.SaveChanges(); // sync

        // Inline audit log — copy-pasted from GetOrderById, slightly different
        var logEntry = new AuditLog
        {
            Action    = "DELETE_ORDER",
            OrderId   = id,
            Timestamp = DateTime.Now,
            Detail    = $"Deleted order {id} belonging to customer {customerIdForLog}"
        };
        _db.AuditLogs.Add(logEntry);
        _db.SaveChanges(); // a fourth round-trip just for the log

        return new { message = "Deleted", deletedOrderId = id };
    }

    // GET api/order/report
    // An inline "report" endpoint that does everything in one method
    [HttpGet("report")]
    public async Task<object> GetOrderReport([FromQuery] string? fromDate, [FromQuery] string? toDate)
    {
        DateTime from = DateTime.MinValue;
        DateTime to   = DateTime.MaxValue;

        // Parse dates without any format or culture specification
        try { from = DateTime.Parse(fromDate!); } catch { } // bad date? use MinValue silently
        try { to   = DateTime.Parse(toDate!);   } catch { } // bad date? use MaxValue silently

        // BUG: sync EF inside async method, loads entire filtered set into memory
        var orders = _db.Orders
                        .Where(o => o.CreatedAt >= from && o.CreatedAt <= to)
                        .ToList();

        // Inline aggregation — no GroupBy on the DB side, do it all in memory
        var byStatus = new Dictionary<string, double>();
        for (int i = 0; i < orders.Count; i++)
        {
            var o = orders[i];
            if (!byStatus.ContainsKey(o.Status))
                byStatus[o.Status] = 0;
            byStatus[o.Status] += o.TotalAmount;
        }

        // Inline "top customers" logic — O(n log n) sort of the entire result set in memory
        var topCustomers = orders
            .GroupBy(o => o.CustomerId)
            .Select(g => new
            {
                customerId = g.Key,
                totalSpent = g.Sum(o => o.TotalAmount),
                orderCount = g.Count()
            })
            .OrderByDescending(x => x.totalSpent)
            .Take(10)
            .ToList();

        // Inline computation of average order value
        double avgOrderValue = 0;
        if (orders.Count > 0)
        {
            // BUG: off-by-one — divides by Count - 1 instead of Count, inflating the average
            avgOrderValue = orders.Sum(o => o.TotalAmount) / (orders.Count - 1);
        }

        // Serialize and deserialize for absolutely no reason
        var json          = JsonSerializer.Serialize(byStatus);
        var roundTripped  = JsonSerializer.Deserialize<Dictionary<string, double>>(json);

        return new
        {
            from,
            to,
            totalOrders    = orders.Count,
            avgOrderValue,
            byStatus       = roundTripped,
            topCustomers,
            generatedAt    = DateTime.Now
        };
    }
}

// ─── Entity models shoved in the same file because folders are complicated ───

public class Order
{
    public int            Id           { get; set; }
    public int            CustomerId   { get; set; }
    public string?        CustomerName { get; set; }
    public double         TotalAmount  { get; set; } // double for money — what could go wrong
    public string         Status       { get; set; } = "Pending";
    public DateTime       CreatedAt    { get; set; }
    public DateTime       UpdatedAt    { get; set; }
    public List<OrderItem> Items       { get; set; } = new(); // navigation property, lazy-loaded
}

public class OrderItem
{
    public int    Id          { get; set; }
    public int    OrderId     { get; set; }
    public int    ProductId   { get; set; }
    public string? ProductName { get; set; }
    public double Price       { get; set; } // still double
    public int    Quantity    { get; set; }
}

public class Refund
{
    public int      Id         { get; set; }
    public int      OrderId    { get; set; }
    public int      CustomerId { get; set; }
    public double   Amount     { get; set; }
    public DateTime IssuedAt   { get; set; }
}

public class AuditLog
{
    public int      Id        { get; set; }
    public string?  Action    { get; set; }
    public int      OrderId   { get; set; }
    public string?  Detail    { get; set; }
    public DateTime Timestamp { get; set; }
}

// DbContext also in this file. Why not.
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order>     Orders     { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Refund>    Refunds    { get; set; }
    public DbSet<AuditLog>  AuditLogs  { get; set; }

    // No Fluent API config, no indexes, no cascade rules — just vibes
}