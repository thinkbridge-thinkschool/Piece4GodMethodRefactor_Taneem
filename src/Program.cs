using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Orders.Data;
using Orders.Exceptions;
using Orders.Repositories;
using Orders.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "OrderApi", Version = "v1" });
});

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite(
        builder.Configuration.GetConnectionString("Default")
            ?? "Data Source=orders.db"));

builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService,    OrderService>();

// ── App ───────────────────────────────────────────────────────────────────────

var app = builder.Build();

// Global exception handler — converts unhandled exceptions to RFC 9457 ProblemDetails.
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
    var ex      = feature?.Error;

    ctx.Response.ContentType = "application/problem+json";

    (ctx.Response.StatusCode, var title, var detail) = ex switch
    {
        OrderNotFoundException e    => (404, "Not found.", e.Message),
        OrderBusinessRuleException e => (422, "Business rule violation.", e.Message),
        OperationCanceledException  => (499, "Request cancelled.", "The request was cancelled."),
        _                           => (500, "An unexpected error occurred.", null)
    };

    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    if (ctx.Response.StatusCode == 500)
        logger.LogError(ex, "Unhandled exception");

    await ctx.Response.WriteAsJsonAsync(new
    {
        type   = "https://tools.ietf.org/html/rfc9110",
        title,
        status = ctx.Response.StatusCode,
        detail
    });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}



//app.UseHttpsRedirection();
app.MapControllers();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}
app.Run();

// Expose Program as partial so WebApplicationFactory can find it in tests.
public partial class Program { }