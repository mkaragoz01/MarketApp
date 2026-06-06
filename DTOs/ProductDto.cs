namespace MarketApp.DTOs;

/// <summary>
/// API yanıtında dönen ürün bilgisi (liste ve detay).
/// </summary>
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
