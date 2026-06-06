using MarketApp.DTOs;
using MarketApp.Models;
using MarketApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketApp.Controllers;

[ApiController]
[Route("dashboard")]
[Authorize(Roles = UserRoles.Admin)]
public class DashboardController : ControllerBase
{
    private readonly IProductService _productService;

    public DashboardController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary()
    {
        var summary = await _productService.GetDashboardSummaryAsync();
        return Ok(summary);
    }
}
