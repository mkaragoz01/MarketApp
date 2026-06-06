using MarketApp.DTOs;

namespace MarketApp.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync();
    Task<(UserDto? User, UserOperationError Error, string? Message)> CreateAsync(CreateUserDto dto);
    Task<(UserDto? User, UserOperationError Error, string? Message)> UpdateAsync(int id, UpdateUserDto dto);
    Task<(bool Success, UserOperationError Error, string? Message)> DeleteAsync(int id, int currentUserId);
}
