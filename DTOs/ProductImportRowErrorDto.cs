namespace MarketApp.DTOs;

public class ProductImportRowErrorDto
{
    public int RowNumber { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

