using Ventia.Application.Abstractions;
using Ventia.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ventia.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
    // el refresh token vive en una cookie httpOnly (JavaScript no puede leerlo)
    private const string RefreshCookie = "ventia_refresh";
    private const string RefreshCookiePath = "/api/auth";

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await authService.RegisterAsync(request, ct);
        SetRefreshCookie(result);
        return StatusCode(StatusCodes.Status201Created, result.Response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        SetRefreshCookie(result);
        return result.Response;
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var token) || string.IsNullOrEmpty(token))
            return Unauthorized();

        var result = await authService.RefreshAsync(token, ct);
        SetRefreshCookie(result);
        return result.Response;
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(RefreshCookie, out var token) && !string.IsNullOrEmpty(token))
            await authService.LogoutAsync(token, ct);

        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = RefreshCookiePath });
        return NoContent();
    }

    /// <summary>cualquier cuenta con sesion (dueno o cajero o superadmin)</summary>
    [HttpGet("me")]
    [Authorize]
    public Task<UserDto> Me(CancellationToken ct) => authService.GetUserAsync(currentUser.UserId!.Value, ct);

    /// <summary>cambiar la contrasena (es lo unico que puede hacer una cuenta con contrasena temporal)</summary>
    [HttpPost("change-password")]
    [Authorize]
    [EnableRateLimiting(RateLimits.Auth)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await authService.ChangePasswordAsync(currentUser.UserId!.Value, request, ct);
        SetRefreshCookie(result);
        return result.Response;
    }

    private void SetRefreshCookie(AuthResult result) =>
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshCookiePath,
            Expires = result.RefreshTokenExpiresAt
        });
}
