namespace MarketApp.DTOs;

public class DashboardSummaryDto
{
    public int TotalProductCount { get; set; }
    public int TotalStock { get; set; }
    public decimal TotalStockValue { get; set; }
    public ProductDto? MostExpensiveProduct { get; set; }
    public int LowStockThreshold { get; set; }
    public IReadOnlyList<ProductDto> LowStockProducts { get; set; } = [];
}
