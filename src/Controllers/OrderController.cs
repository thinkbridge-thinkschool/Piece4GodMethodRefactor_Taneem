
using Microsoft.AspNetCore.Mvc;
using Orders.Models;
using Orders.Services;
using Orders.Dtos;
using Orders.Exceptions;

namespace Orders.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class OrderController : ControllerBase
{
    private readonly IOrderService _service;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IOrderService service, ILogger<OrderController> logger)
    {
        _service = service;
        _logger  = logger;
    }

    /// <summary>Returns a paginated list of all orders, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrderSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrderSummaryResponse>>> GetAllOrders(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _service.GetAllAsync(page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Returns the full detail for a single order, including its items.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailResponse>> GetOrderById(
        int id, CancellationToken ct)
    {
        try
        {
            var result = await _service.GetByIdAsync(id, ct);
            return Ok(result);
        }
        catch (OrderNotFoundException ex)
        {
            _logger.LogWarning("GetOrderById: {Message}", ex.Message);
            return NotFound(new ProblemDetails
            {
                Title  = "Order not found.",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
    }

    /// <summary>Creates a new order. Validates the request before touching the database.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        // Model validation (DataAnnotations) is enforced automatically by [ApiController]
        // before this method body runs — invalid requests never reach the service.
        try
        {
            var result = await _service.CreateAsync(request, ct);
            return CreatedAtAction(
                nameof(GetOrderById),
                new { id = result.OrderId },
                result);
        }
        catch (OrderBusinessRuleException ex)
        {
            _logger.LogWarning("CreateOrder business rule violation: {Message}", ex.Message);
            return UnprocessableEntity(new ProblemDetails
            {
                Title  = "Business rule violation.",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }

    /// <summary>Updates the status of an existing order.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(UpdateOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UpdateOrderResponse>> UpdateOrder(
        int id, [FromBody] UpdateOrderRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _service.UpdateStatusAsync(id, request, ct);
            return Ok(result);
        }
        catch (OrderNotFoundException ex)
        {
            _logger.LogWarning("UpdateOrder: {Message}", ex.Message);
            return NotFound(new ProblemDetails
            {
                Title  = "Order not found.",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OrderBusinessRuleException ex)
        {
            _logger.LogWarning("UpdateOrder business rule violation: {Message}", ex.Message);
            return UnprocessableEntity(new ProblemDetails
            {
                Title  = "Business rule violation.",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }

    /// <summary>Deletes an order that has not yet been delivered.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(DeleteOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DeleteOrderResponse>> DeleteOrder(
        int id, CancellationToken ct)
    {
        try
        {
            var result = await _service.DeleteAsync(id, ct);
            return Ok(result);
        }
        catch (OrderNotFoundException ex)
        {
            _logger.LogWarning("DeleteOrder: {Message}", ex.Message);
            return NotFound(new ProblemDetails
            {
                Title  = "Order not found.",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (OrderBusinessRuleException ex)
        {
            _logger.LogWarning("DeleteOrder business rule violation: {Message}", ex.Message);
            return UnprocessableEntity(new ProblemDetails
            {
                Title  = "Business rule violation.",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }

    /// <summary>Returns aggregated order totals and top customers for a date range.</summary>
    [HttpGet("report")]
    [ProducesResponseType(typeof(OrderReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderReportResponse>> GetOrderReport(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
    {
        var effectiveFrom = from ?? DateTimeOffset.UtcNow.AddMonths(-1);
        var effectiveTo   = to   ?? DateTimeOffset.UtcNow;

        if (effectiveFrom > effectiveTo)
        {
            ModelState.AddModelError(nameof(from), "'from' must be earlier than 'to'.");
            return ValidationProblem(ModelState);
        }

        var result = await _service.GetReportAsync(effectiveFrom, effectiveTo, ct);
        return Ok(result);
    }
}
