using System.ComponentModel.DataAnnotations;

namespace MarketApp.DTOs;

/// <summary>
/// Yeni ürün ekleme isteği. Id ve SortOrder sunucuda atanır.
/// </summary>
public class CreateProductDto
{
    [Required(ErrorMessage = "Ürün adı zorunludur.")]
    [RegularExpression(@".*\S.*", ErrorMessage = "Ürün adı boş olamaz.")]
    [StringLength(100, ErrorMessage = "Ürün adı en fazla 100 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, double.MaxValue, ErrorMessage = "Fiyat 0'dan büyük olmalıdır.")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Stok negatif olamaz.")]
    public int Stock { get; set; }

    [Required(ErrorMessage = "Birim zorunludur.")]
    [RegularExpression(@".*\S.*", ErrorMessage = "Birim boş olamaz.")]
    [StringLength(30, ErrorMessage = "Birim en fazla 30 karakter olabilir.")]
    public string Unit { get; set; } = string.Empty;
}
