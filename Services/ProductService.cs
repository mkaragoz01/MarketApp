using MarketApp.Data;
using MarketApp.DTOs;
using MarketApp.Models;
using Microsoft.EntityFrameworkCore;

namespace MarketApp.Services;

public class ProductService : IProductService
{
    private const int LowStockThreshold = 10;
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly ILogger<ProductService> _logger;

    public ProductService(AppDbContext context, ILogger<ProductService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<PagedResultDto<ProductDto>> GetPagedAsync(int page, int pageSize, string? search)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            query = query.Where(p => p.Name.Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync();
        var totalPages = totalCount == 0
            ? 1
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        if (page > totalPages)
            page = totalPages;

        var products = await query
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = new PagedResultDto<ProductDto>
        {
            Items = products.Select(ToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };

        _logger.LogInformation(
            "Products paged. Page: {Page}, PageSize: {PageSize}, Search: {Search}, ReturnedCount: {ReturnedCount}, TotalCount: {TotalCount}",
            result.Page,
            result.PageSize,
            search,
            result.Items.Count,
            result.TotalCount);

        return result;
    }

    public async Task<ProductDto?> GetByIdAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);
        return product is null ? null : ToDto(product);
    }

    public async Task<(ProductDto? Product, ProductOperationError Error, string? Message)> CreateAsync(
        CreateProductDto dto)
    {
        var name = dto.Name.Trim();

        if (await NameExistsAsync(name))
        {
            _logger.LogWarning("Product create failed because name already exists. Name: {ProductName}", name);
            return (null, ProductOperationError.DuplicateName, "Bu isimde bir ürün zaten mevcut.");
        }

        var maxOrder = await _context.Products.MaxAsync(p => (int?)p.SortOrder) ?? 0;

        var product = new Product
        {
            Name = name,
            Price = dto.Price,
            Stock = dto.Stock,
            Unit = dto.Unit.Trim(),
            SortOrder = maxOrder + 1
        };

        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Product created. ProductId: {ProductId}, Name: {ProductName}, Unit: {Unit}, Price: {Price}, Stock: {Stock}",
            product.Id,
            product.Name,
            product.Unit,
            product.Price,
            product.Stock);

        return (ToDto(product), ProductOperationError.None, null);
    }

    public async Task<(bool Success, ProductOperationError Error, string? Message)> UpdateAsync(
        int id, UpdateProductDto dto)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null)
        {
            _logger.LogWarning("Product update failed because product was not found. ProductId: {ProductId}", id);
            return (false, ProductOperationError.NotFound, null);
        }

        var name = dto.Name.Trim();

        if (await NameExistsAsync(name, excludeId: id))
        {
            _logger.LogWarning(
                "Product update failed because name already exists. ProductId: {ProductId}, Name: {ProductName}",
                id,
                name);

            return (false, ProductOperationError.DuplicateName, "Bu isimde bir ürün zaten mevcut.");
        }

        var oldName = product.Name;
        var oldStock = product.Stock;
        product.Name = name;
        product.Price = dto.Price;
        product.Stock = dto.Stock;
        product.Unit = dto.Unit.Trim();

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Product updated. ProductId: {ProductId}, OldName: {OldName}, NewName: {NewName}, OldStock: {OldStock}, NewStock: {NewStock}",
            product.Id,
            oldName,
            product.Name,
            oldStock,
            product.Stock);

        return (true, ProductOperationError.None, null);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null)
        {
            _logger.LogWarning("Product delete failed because product was not found. ProductId: {ProductId}", id);
            return false;
        }

        var productName = product.Name;
        _context.Products.Remove(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Product deleted. ProductId: {ProductId}, Name: {ProductName}",
            id,
            productName);

        return true;
    }

    public async Task<(bool Success, ProductOperationError Error, string? Message)> ReorderAsync(
        ReorderProductsRequest request)
    {
        if (request.ProductIds.Count == 0)
        {
            _logger.LogWarning("Product reorder failed because product id list is empty.");
            return (false, ProductOperationError.InvalidRequest, "Ürün listesi boş olamaz.");
        }

        var products = await _context.Products
            .Where(p => request.ProductIds.Contains(p.Id))
            .ToListAsync();

        if (products.Count != request.ProductIds.Count)
        {
            _logger.LogWarning(
                "Product reorder failed because product id list contains invalid ids. RequestedCount: {RequestedCount}, FoundCount: {FoundCount}",
                request.ProductIds.Count,
                products.Count);

            return (false, ProductOperationError.InvalidRequest, "Geçersiz ürün kimliği içeriyor.");
        }

        var sortOrderSlots = products
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .Select(p => p.SortOrder)
            .ToList();

        for (var i = 0; i < request.ProductIds.Count; i++)
        {
            var product = products.First(p => p.Id == request.ProductIds[i]);
            product.SortOrder = sortOrderSlots[i];
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Products reordered. ProductCount: {ProductCount}",
            request.ProductIds.Count);

        return (true, ProductOperationError.None, null);
    }

    public async Task<(ProductDto? Product, ProductOperationError Error, string? Message)> AdjustStockAsync(
        int id, int delta)
    {
        var product = await _context.Products.FindAsync(id);
        if (product is null)
        {
            _logger.LogWarning("Stock update failed because product was not found. ProductId: {ProductId}", id);
            return (null, ProductOperationError.NotFound, null);
        }

        var newStock = Math.Max(0, product.Stock + delta);
        if (newStock == product.Stock)
        {
            _logger.LogInformation(
                "Stock update skipped because stock did not change. ProductId: {ProductId}, Stock: {Stock}, Delta: {Delta}",
                product.Id,
                product.Stock,
                delta);

            return (ToDto(product), ProductOperationError.None, null);
        }

        var oldStock = product.Stock;
        product.Stock = newStock;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Stock updated. ProductId: {ProductId}, OldStock: {OldStock}, NewStock: {NewStock}, Delta: {Delta}",
            product.Id,
            oldStock,
            product.Stock,
            delta);

        return (ToDto(product), ProductOperationError.None, null);
    }

    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync()
    {
        var products = await _context.Products
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToListAsync();

        var mostExpensiveProduct = products
            .OrderByDescending(p => p.Price)
            .ThenBy(p => p.Name)
            .FirstOrDefault();

        var lowStockProducts = products
            .Where(p => p.Stock < LowStockThreshold)
            .OrderBy(p => p.Stock)
            .ThenBy(p => p.Name)
            .Select(ToDto)
            .ToList();

        var summary = new DashboardSummaryDto
        {
            TotalProductCount = products.Count,
            TotalStock = products.Sum(p => p.Stock),
            TotalStockValue = products.Sum(p => p.Price * p.Stock),
            MostExpensiveProduct = mostExpensiveProduct is null ? null : ToDto(mostExpensiveProduct),
            LowStockThreshold = LowStockThreshold,
            LowStockProducts = lowStockProducts
        };

        _logger.LogInformation(
            "Dashboard summary calculated. ProductCount: {ProductCount}, TotalStock: {TotalStock}, LowStockCount: {LowStockCount}",
            summary.TotalProductCount,
            summary.TotalStock,
            summary.LowStockProducts.Count);

        return summary;
    }

    private static ProductDto ToDto(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Price = product.Price,
        Stock = product.Stock,
        Unit = product.Unit,
        SortOrder = product.SortOrder
    };

    private async Task<bool> NameExistsAsync(string name, int? excludeId = null)
    {
        var normalized = name.Trim().ToLower();

        return await _context.Products.AnyAsync(p =>
            p.Name.ToLower() == normalized &&
            (excludeId == null || p.Id != excludeId));
    }
}
