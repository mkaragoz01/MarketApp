using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarketApp.DTOs;
using MarketApp.Models;
using MarketApp.Services;

namespace MarketApp.Controllers;

[ApiController]
[Route("products")]
public class ProductController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PagedResultDto<ProductDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? unit = null,
        [FromQuery] string? sort = null)
    {
        var products = await _productService.GetPagedAsync(page, pageSize, search, unit, sort);
        return Ok(products);
    }

    [HttpGet("{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<ProductDto>> GetById(int id)
    {
        var product = await _productService.GetByIdAsync(id);
        if (product is null)
            return NotFound();

        return Ok(product);
    }

    [HttpGet("export")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Export(
        [FromQuery] string? search = null,
        [FromQuery] string? unit = null,
        [FromQuery] string? sort = null)
    {
        var fileBytes = await _productService.ExportExcelAsync(search, unit, sort);
        var fileName = $"products-{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";

        return File(
            fileBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost("import")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<ProductImportResultDto>> Import([FromForm] IFormFile? file)
    {
        file ??= Request.HasFormContentType
            ? Request.Form.Files.GetFile("file") ?? Request.Form.Files.FirstOrDefault()
            : null;

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Excel dosyası boş olamaz." });

        try
        {
            var result = await _productService.ImportExcelAsync(file.OpenReadStream(), file.FileName);
            return Ok(result);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<ProductDto>> Create(CreateProductDto dto)
    {
        var (created, error, message) = await _productService.CreateAsync(dto);

        if (error == ProductOperationError.DuplicateName)
            return BadRequest(new { message });

        return CreatedAtAction(nameof(GetById), new { id = created!.Id }, created);
    }

    [HttpPut("reorder")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Reorder(ReorderProductsRequest request)
    {
        var (_, error, message) = await _productService.ReorderAsync(request);

        if (error == ProductOperationError.InvalidRequest)
            return BadRequest(new { message });

        return NoContent();
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Update(int id, UpdateProductDto dto)
    {
        var (_, error, message) = await _productService.UpdateAsync(id, dto);

        if (error == ProductOperationError.NotFound)
            return NotFound();

        if (error == ProductOperationError.DuplicateName)
            return BadRequest(new { message });

        return NoContent();
    }

    [HttpPatch("{id:int}/stock")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<ProductDto>> AdjustStock(int id, [FromQuery] int delta)
    {
        var (product, error, _) = await _productService.AdjustStockAsync(id, delta);

        if (error == ProductOperationError.NotFound)
            return NotFound();

        return Ok(product);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _productService.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
