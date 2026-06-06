using System.Security.Claims;
using MarketApp.DTOs;
using MarketApp.Models;
using MarketApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MarketApp.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = UserRoles.Admin)]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll()
    {
        var users = await _userService.GetAllAsync();
        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserDto dto)
    {
        var (user, error, message) = await _userService.CreateAsync(dto);

        if (error is UserOperationError.DuplicateUsername or UserOperationError.InvalidRole)
            return BadRequest(new { message });

        return CreatedAtAction(nameof(GetAll), new { id = user!.Id }, user);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserDto dto)
    {
        var (user, error, message) = await _userService.UpdateAsync(id, dto);

        if (error == UserOperationError.NotFound)
            return NotFound();

        if (error is UserOperationError.DuplicateUsername
            or UserOperationError.InvalidRole
            or UserOperationError.LastAdmin)
            return BadRequest(new { message });

        return Ok(user);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var currentUserId = GetCurrentUserId();
        var (_, error, message) = await _userService.DeleteAsync(id, currentUserId);

        if (error == UserOperationError.NotFound)
            return NotFound();

        if (error is UserOperationError.LastAdmin or UserOperationError.SelfDelete)
            return BadRequest(new { message });

        return NoContent();
    }

    private int GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var userId) ? userId : 0;
    }
}
