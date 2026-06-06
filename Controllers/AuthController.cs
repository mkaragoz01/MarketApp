using Microsoft.AspNetCore.Mvc;
using MarketApp.DTOs;
using MarketApp.Services;

namespace MarketApp.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        var (response, error) = await _authService.RegisterAsync(dto);
        if (error is not null)
            return BadRequest(new { message = error });

        return Ok(response);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var (response, error) = await _authService.LoginAsync(dto);
        if (error is not null)
            return BadRequest(new { message = error });

        return Ok(response);
    }
}
