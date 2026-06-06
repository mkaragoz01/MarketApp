using MarketApp.DTOs;

namespace MarketApp.Services;

public interface IAuthService
{
    Task<(AuthResponseDto? Response, string? ErrorMessage)> RegisterAsync(RegisterDto dto);
    Task<(AuthResponseDto? Response, string? ErrorMessage)> LoginAsync(LoginDto dto);
}
