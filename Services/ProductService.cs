using MarketApp.Data;
using MarketApp.DTOs;
using MarketApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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

    public async Task<PagedResultDto<ProductDto>> GetPagedAsync(
        int page,
        int pageSize,
        string? search,
        string? unit,
        string? sort)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = ApplyFilters(_context.Products.AsNoTracking(), search, unit);

        var totalCount = await query.CountAsync();
        var totalPages = totalCount == 0
            ? 1
            : (int)Math.Ceiling(totalCount / (double)pageSize);

        if (page > totalPages)
            page = totalPages;

        var products = await ApplySort(query, sort)
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
            "Products paged. Page: {Page}, PageSize: {PageSize}, Search: {Search}, Unit: {Unit}, Sort: {Sort}, ReturnedCount: {ReturnedCount}, TotalCount: {TotalCount}",
            result.Page,
            result.PageSize,
            search,
            unit,
            sort,
            result.Items.Count,
            result.TotalCount);

        return result;
    }

    public async Task<byte[]> ExportExcelAsync(string? search, string? unit, string? sort)
    {
        var products = await ApplySort(ApplyFilters(_context.Products.AsNoTracking(), search, unit), sort)
            .ToListAsync();
        var productDtos = products.Select(ToDto).ToList();

        _logger.LogInformation(
            "Products exported to Excel. Search: {Search}, Unit: {Unit}, Sort: {Sort}, ProductCount: {ProductCount}",
            search,
            unit,
            sort,
            productDtos.Count);

        return ProductExcelWorkbook.CreateProductsWorkbook(productDtos);
    }

    public async Task<ProductImportResultDto> ImportExcelAsync(Stream stream, string fileName)
    {
        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Sadece .xlsx Excel dosyası yüklenebilir.");

        var rows = ProductExcelWorkbook.ReadProducts(stream);
        var result = new ProductImportResultDto
        {
            TotalRows = rows.Count
        };

        if (rows.Count == 0)
            return result;

        var existingProducts = await _context.Products.ToListAsync();
        var productsById = existingProducts.ToDictionary(p => p.Id);
        var productsByName = existingProducts
            .GroupBy(p => NormalizeProductName(p.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var maxOrder = await _context.Products.MaxAsync(p => (int?)p.SortOrder) ?? 0;
        var productsToAdd = new List<Product>();

        foreach (var row in rows)
        {
            var existingProduct = FindExistingProduct(row, productsById, productsByName);
            if (existingProduct is not null)
            {
                if (TryUpdateProductFromImport(row, existingProduct, productsByName, result))
                    result.UpdatedCount++;
                else
                    result.SkippedCount++;

                continue;
            }

            if (!TryCreateProductFromImport(row, result, out var newProduct))
                continue;

            maxOrder++;
            newProduct.SortOrder = maxOrder;
            productsToAdd.Add(newProduct);
            productsByName[NormalizeProductName(newProduct.Name)] = newProduct;
        }

        if (productsToAdd.Count > 0 || result.UpdatedCount > 0)
        {
            _context.Products.AddRange(productsToAdd);
            await _context.SaveChangesAsync();
        }

        result.ImportedCount = productsToAdd.Count;

        _logger.LogInformation(
            "Products imported from Excel. FileName: {FileName}, TotalRows: {TotalRows}, ImportedCount: {ImportedCount}, UpdatedCount: {UpdatedCount}, SkippedCount: {SkippedCount}, ErrorCount: {ErrorCount}",
            fileName,
            result.TotalRows,
            result.ImportedCount,
            result.UpdatedCount,
            result.SkippedCount,
            result.ErrorCount);

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

    private static IQueryable<Product> ApplyFilters(IQueryable<Product> query, string? search, string? unit)
    {
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            query = query.Where(p => p.Name.Contains(normalizedSearch));
        }

        if (!string.IsNullOrWhiteSpace(unit))
        {
            var normalizedUnit = unit.Trim();
            query = query.Where(p => p.Unit == normalizedUnit);
        }

        return query;
    }

    private static IOrderedQueryable<Product> ApplySort(IQueryable<Product> query, string? sort)
    {
        return sort?.Trim().ToLowerInvariant() switch
        {
            "name" or "name_asc" => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
            "name_desc" => query.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
            "price" or "price_asc" => query.OrderBy(p => p.Price).ThenBy(p => p.SortOrder).ThenBy(p => p.Id),
            "price_desc" => query.OrderByDescending(p => p.Price).ThenBy(p => p.SortOrder).ThenBy(p => p.Id),
            "stock" or "stock_asc" => query.OrderBy(p => p.Stock).ThenBy(p => p.SortOrder).ThenBy(p => p.Id),
            "stock_desc" => query.OrderByDescending(p => p.Stock).ThenBy(p => p.SortOrder).ThenBy(p => p.Id),
            "unit" or "unit_asc" => query.OrderBy(p => p.Unit).ThenBy(p => p.SortOrder).ThenBy(p => p.Id),
            "unit_desc" => query.OrderByDescending(p => p.Unit).ThenBy(p => p.SortOrder).ThenBy(p => p.Id),
            _ => query.OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
        };
    }

    private static Product? FindExistingProduct(
        ProductImportRow row,
        IReadOnlyDictionary<int, Product> productsById,
        IReadOnlyDictionary<string, Product> productsByName)
    {
        if (HasValue(row.Id) && TryParseWholeNumber(row.Id, out var id) && productsById.TryGetValue(id, out var productById))
            return productById;

        if (HasValue(row.Name) && productsByName.TryGetValue(NormalizeProductName(row.Name), out var productByName))
            return productByName;

        return null;
    }

    private static bool TryUpdateProductFromImport(
        ProductImportRow row,
        Product product,
        Dictionary<string, Product> productsByName,
        ProductImportResultDto result)
    {
        var changed = false;

        if (HasValue(row.Name))
        {
            var newName = row.Name.Trim();
            if (newName.Length > 100)
            {
                AddImportError(result, row, "Ürün adı en fazla 100 karakter olabilir.");
                return false;
            }

            var normalizedOldName = NormalizeProductName(product.Name);
            var normalizedNewName = NormalizeProductName(newName);
            if (normalizedNewName != normalizedOldName)
            {
                if (productsByName.TryGetValue(normalizedNewName, out var duplicate) && duplicate.Id != product.Id)
                {
                    AddImportError(result, row, "Bu isimde başka bir ürün zaten mevcut.");
                    return false;
                }

                productsByName.Remove(normalizedOldName);
                product.Name = newName;
                productsByName[normalizedNewName] = product;
                changed = true;
            }
        }

        if (HasValue(row.Unit))
        {
            var unit = row.Unit.Trim();
            if (unit.Length > 30)
            {
                AddImportError(result, row, "Birim en fazla 30 karakter olabilir.");
                return false;
            }

            if (product.Unit != unit)
            {
                product.Unit = unit;
                changed = true;
            }
        }

        if (HasValue(row.Price))
        {
            if (!TryParseDecimal(row.Price, out var price) || price <= 0)
            {
                AddImportError(result, row, "Fiyat 0'dan büyük olmalıdır.");
                return false;
            }

            if (product.Price != price)
            {
                product.Price = price;
                changed = true;
            }
        }

        if (HasValue(row.Stock))
        {
            if (!TryParseStock(row.Stock, out var stock) || stock < 0)
            {
                AddImportError(result, row, "Stok negatif olmayan tam sayı olmalıdır.");
                return false;
            }

            if (product.Stock != stock)
            {
                product.Stock = stock;
                changed = true;
            }
        }

        return changed;
    }

    private static bool TryCreateProductFromImport(
        ProductImportRow row,
        ProductImportResultDto result,
        out Product product)
    {
        product = new Product();

        if (!HasValue(row.Id) && !HasValue(row.Name))
        {
            result.SkippedCount++;
            return false;
        }

        if (!HasValue(row.Name))
        {
            AddImportError(result, row, "Yeni ürün eklemek için ürün adı gereklidir.");
            return false;
        }

        var name = row.Name.Trim();
        if (name.Length > 100)
        {
            AddImportError(result, row, "Ürün adı en fazla 100 karakter olabilir.");
            return false;
        }

        var unit = HasValue(row.Unit) ? row.Unit.Trim() : "Adet";
        if (unit.Length > 30)
        {
            AddImportError(result, row, "Birim en fazla 30 karakter olabilir.");
            return false;
        }

        var price = 0.01m;
        if (HasValue(row.Price) && (!TryParseDecimal(row.Price, out price) || price <= 0))
        {
            AddImportError(result, row, "Fiyat 0'dan büyük olmalıdır.");
            return false;
        }

        var stock = 0;
        if (HasValue(row.Stock) && (!TryParseStock(row.Stock, out stock) || stock < 0))
        {
            AddImportError(result, row, "Stok negatif olmayan tam sayı olmalıdır.");
            return false;
        }

        product = new Product
        {
            Name = name,
            Unit = unit,
            Price = price,
            Stock = stock
        };

        return true;
    }

    private static void AddImportError(ProductImportResultDto result, ProductImportRow row, string message)
    {
        result.Errors.Add(new ProductImportRowErrorDto
        {
            RowNumber = row.RowNumber,
            ProductName = row.Name,
            Message = message
        });
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        value = value.Trim();
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out result) ||
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseStock(string value, out int result)
    {
        value = value.Trim();
        return TryParseWholeNumber(value, out result);
    }

    private static bool TryParseWholeNumber(string value, out int result)
    {
        value = value.Trim();
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ||
            int.TryParse(value, NumberStyles.Integer, CultureInfo.GetCultureInfo("tr-TR"), out result))
        {
            return true;
        }

        if (TryParseDecimal(value, out var decimalValue) && decimalValue == decimal.Truncate(decimalValue))
        {
            result = (int)decimalValue;
            return true;
        }

        result = 0;
        return false;
    }

    private static string NormalizeProductName(string name) =>
        name.Trim().ToLowerInvariant();

    private static bool HasValue(string value) =>
        !string.IsNullOrWhiteSpace(value);

    private async Task<bool> NameExistsAsync(string name, int? excludeId = null)
    {
        var normalized = name.Trim().ToLower();

        return await _context.Products.AnyAsync(p =>
            p.Name.ToLower() == normalized &&
            (excludeId == null || p.Id != excludeId));
    }
}
