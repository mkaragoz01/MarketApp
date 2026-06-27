namespace MarketApp.DTOs;

public class ProductImportResultDto
{
    public int TotalRows { get; set; }
    public int ImportedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<ProductImportRowErrorDto> Errors { get; set; } = [];
    public int ErrorCount => Errors.Count;
}
