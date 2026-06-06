using MarketApp.DTOs;
using MarketApp.Models;

namespace MarketApp.Services;

public interface IProductService
{
    Task<PagedResultDto<ProductDto>> GetPagedAsync(int page, int pageSize, string? search);
    Task<ProductDto?> GetByIdAsync(int id);
    Task<(ProductDto? Product, ProductOperationError Error, string? Message)> CreateAsync(CreateProductDto dto);
    Task<(bool Success, ProductOperationError Error, string? Message)> UpdateAsync(int id, UpdateProductDto dto);
    Task<bool> DeleteAsync(int id);
    Task<(bool Success, ProductOperationError Error, string? Message)> ReorderAsync(ReorderProductsRequest request);
    Task<(ProductDto? Product, ProductOperationError Error, string? Message)> AdjustStockAsync(int id, int delta);
    Task<DashboardSummaryDto> GetDashboardSummaryAsync();
}
