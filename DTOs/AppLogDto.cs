namespace MarketApp.DTOs;

public class AppLogDto
{
    public int Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? TraceId { get; set; }
    public string? Method { get; set; }
    public string? Path { get; set; }
    public int? UserId { get; set; }
    public string? Username { get; set; }
}
