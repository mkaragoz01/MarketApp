namespace MarketApp.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Unit { get; set; } = "Adet";
    public int SortOrder { get; set; }
}
