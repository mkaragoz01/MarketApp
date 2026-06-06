using MarketApp.Data;
using MarketApp.DTOs;
using MarketApp.Models;
using Microsoft.EntityFrameworkCore;

namespace MarketApp.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext context, ILogger<UserService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserDto>> GetAllAsync()
    {
        var users = await _context.Users
            .OrderBy(u => u.Username)
            .ToListAsync();

        return users.Select(ToDto).ToList();
    }

    public async Task<(UserDto? User, UserOperationError Error, string? Message)> CreateAsync(CreateUserDto dto)
    {
        var username = dto.Username.Trim();
        var role = NormalizeRole(dto.Role);

        if (!IsValidRole(role))
        {
            _logger.LogWarning(
                "User create failed because role is invalid. Username: {Username}, Role: {Role}",
                username,
                dto.Role);

            return (null, UserOperationError.InvalidRole, "Rol Admin veya User olmalıdır.");
        }

        if (await UsernameExistsAsync(username))
        {
            _logger.LogWarning("User create failed because username already exists. Username: {Username}", username);
            return (null, UserOperationError.DuplicateUsername, "Bu kullanıcı adı zaten kayıtlı.");
        }

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = role
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User created by admin workflow. UserId: {UserId}, Username: {Username}, Role: {Role}",
            user.Id,
            user.Username,
            user.Role);

        return (ToDto(user), UserOperationError.None, null);
    }

    public async Task<(UserDto? User, UserOperationError Error, string? Message)> UpdateAsync(
        int id, UpdateUserDto dto)
    {
        var user = await _context.Users.FindAsync(id);
        if (user is null)
        {
            _logger.LogWarning("User update failed because user was not found. UserId: {UserId}", id);
            return (null, UserOperationError.NotFound, null);
        }

        var username = dto.Username.Trim();
        var role = NormalizeRole(dto.Role);

        if (!IsValidRole(role))
        {
            _logger.LogWarning(
                "User update failed because role is invalid. UserId: {UserId}, Role: {Role}",
                id,
                dto.Role);

            return (null, UserOperationError.InvalidRole, "Rol Admin veya User olmalıdır.");
        }

        if (await UsernameExistsAsync(username, excludeId: id))
        {
            _logger.LogWarning(
                "User update failed because username already exists. UserId: {UserId}, Username: {Username}",
                id,
                username);

            return (null, UserOperationError.DuplicateUsername, "Bu kullanıcı adı zaten kayıtlı.");
        }

        if (user.Role == UserRoles.Admin && role != UserRoles.Admin && !await HasAnotherAdminAsync(id))
        {
            _logger.LogWarning(
                "User update failed because last admin cannot be downgraded. UserId: {UserId}, Username: {Username}",
                user.Id,
                user.Username);

            return (null, UserOperationError.LastAdmin, "Son admin kullanıcının rolü User yapılamaz.");
        }

        var oldUsername = user.Username;
        var oldRole = user.Role;
        var passwordChanged = !string.IsNullOrWhiteSpace(dto.Password);
        user.Username = username;
        user.Role = role;

        if (passwordChanged)
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User updated. UserId: {UserId}, OldUsername: {OldUsername}, NewUsername: {NewUsername}, OldRole: {OldRole}, NewRole: {NewRole}, PasswordChanged: {PasswordChanged}",
            user.Id,
            oldUsername,
            user.Username,
            oldRole,
            user.Role,
            passwordChanged);

        return (ToDto(user), UserOperationError.None, null);
    }

    public async Task<(bool Success, UserOperationError Error, string? Message)> DeleteAsync(
        int id, int currentUserId)
    {
        var user = await _context.Users.FindAsync(id);
        if (user is null)
        {
            _logger.LogWarning("User delete failed because user was not found. UserId: {UserId}", id);
            return (false, UserOperationError.NotFound, null);
        }

        if (user.Id == currentUserId)
        {
            _logger.LogWarning(
                "User delete failed because current user attempted to delete own account. UserId: {UserId}, Username: {Username}",
                user.Id,
                user.Username);

            return (false, UserOperationError.SelfDelete, "Kendi hesabınızı silemezsiniz.");
        }

        if (user.Role == UserRoles.Admin && !await HasAnotherAdminAsync(id))
        {
            _logger.LogWarning(
                "User delete failed because last admin cannot be deleted. UserId: {UserId}, Username: {Username}",
                user.Id,
                user.Username);

            return (false, UserOperationError.LastAdmin, "Son admin kullanıcı silinemez.");
        }

        var username = user.Username;
        var role = user.Role;
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User deleted. UserId: {UserId}, Username: {Username}, Role: {Role}",
            id,
            username,
            role);

        return (true, UserOperationError.None, null);
    }

    private async Task<bool> UsernameExistsAsync(string username, int? excludeId = null)
    {
        var normalized = username.Trim().ToLower();

        return await _context.Users.AnyAsync(u =>
            u.Username.ToLower() == normalized &&
            (excludeId == null || u.Id != excludeId));
    }

    private async Task<bool> HasAnotherAdminAsync(int userId)
    {
        return await _context.Users.AnyAsync(u => u.Id != userId && u.Role == UserRoles.Admin);
    }

    private static bool IsValidRole(string role)
    {
        return role is UserRoles.Admin or UserRoles.User;
    }

    private static string NormalizeRole(string role)
    {
        if (string.Equals(role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
            return UserRoles.Admin;

        if (string.Equals(role, UserRoles.User, StringComparison.OrdinalIgnoreCase))
            return UserRoles.User;

        return role.Trim();
    }

    private static UserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Role = user.Role
    };
}
