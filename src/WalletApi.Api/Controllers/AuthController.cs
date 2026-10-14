using Microsoft.AspNetCore.Mvc;
using WalletApi.Application.Auth;

namespace WalletApi.Api.Controllers;

public record Credentials(string Email, string Password);

[ApiController, Route("api/auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(Credentials c)
    {
        var (token, error) = await auth.RegisterAsync(c.Email, c.Password);
        if (error is null) return Ok(new { token });
        return error.Contains("already") ? Conflict(error) : BadRequest(error);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(Credentials c) =>
        await auth.LoginAsync(c.Email, c.Password) is { } token ? Ok(new { token }) : Unauthorized();
}
