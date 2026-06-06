namespace MarketApp.Models;

/// <summary>
/// Uygulama kullanıcısı. Şifre veritabanında hash olarak saklanır.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoles.User;
}
