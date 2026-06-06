using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MarketApp.Configuration;
using MarketApp.Data;
using MarketApp.DTOs;
using MarketApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MarketApp.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext context,
        IOptions<JwtSettings> jwtOptions,
        ILogger<AuthService> logger)
    {
        _context = context;
        _jwtSettings = jwtOptions.Value;
        _logger = logger;
    }

    public async Task<(AuthResponseDto? Response, string? ErrorMessage)> RegisterAsync(RegisterDto dto)
    {
        var username = dto.Username.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(dto.Password))
        {
            _logger.LogWarning("Register failed because username or password is empty. Username: {Username}", username);
            return (null, "Kullanıcı adı ve şifre zorunludur.");
        }

        if (dto.Password.Length < 4)
        {
            _logger.LogWarning("Register failed because password is too short. Username: {Username}", username);
            return (null, "Şifre en az 4 karakter olmalıdır.");
        }

        var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower());
        if (exists)
        {
            _logger.LogWarning("Register failed because username already exists. Username: {Username}", username);
            return (null, "Bu kullanıcı adı zaten kayıtlı.");
        }

        var user = new User
        {
            Username = username,
            Role = UserRoles.User,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User registered. UserId: {UserId}, Username: {Username}, Role: {Role}",
            user.Id,
            user.Username,
            user.Role);

        return (CreateToken(user), null);
    }

    public async Task<(AuthResponseDto? Response, string? ErrorMessage)> LoginAsync(LoginDto dto)
    {
        var username = dto.Username.Trim();
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user is null)
        {
            _logger.LogWarning("Login failed because user was not found. Username: {Username}", username);
            return (null, "Kullanıcı adı veya şifre hatalı.");
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            _logger.LogWarning(
                "Login failed because password is invalid. UserId: {UserId}, Username: {Username}",
                user.Id,
                user.Username);

            return (null, "Kullanıcı adı veya şifre hatalı.");
        }

        _logger.LogInformation(
            "User logged in. UserId: {UserId}, Username: {Username}, Role: {Role}",
            user.Id,
            user.Username,
            user.Role);

        return (CreateToken(user), null);
    }

    private AuthResponseDto CreateToken(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpireMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new AuthResponseDto
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            Username = user.Username,
            Role = user.Role,
            ExpiresAt = expiresAt
        };
    }
}
