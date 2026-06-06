namespace MarketApp.DTOs;

/// <summary>
/// Başarılı giriş/kayıt sonrası frontend'e dönen JWT bilgisi.
/// </summary>
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
