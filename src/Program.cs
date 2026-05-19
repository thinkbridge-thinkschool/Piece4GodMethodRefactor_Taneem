using Microsoft.EntityFrameworkCore;
using Orders.Data;
using Orders.Repositories;
using Orders.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database registration
// IMPORTANT:
// During tests we use InMemoryDatabase from WebApplicationFactory.
// So DO NOT register SQLite while environment == Testing.

if (builder.Environment.EnvironmentName != "Testing")
{
    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        options.UseSqlite(
            builder.Configuration.GetConnectionString("DefaultConnection"));
    });
}

// Dependency Injection
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

// Middleware pipeline
if (app.Environment.IsDevelopment() ||
    app.Environment.EnvironmentName == "Testing")
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Required for WebApplicationFactory integration testing
public partial class Program { }