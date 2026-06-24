using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Models;
using GithubMinServer.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AppDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    JwtTokenService jwtTokenService) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IPasswordHasher<User> _passwordHasher = passwordHasher;
    private readonly JwtTokenService _jwtTokenService = jwtTokenService;

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedUsername = request.Username.Trim();
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var exists = await _dbContext.Users.AnyAsync(
            user => user.Username.ToLower() == normalizedUsername.ToLower() || user.Email.ToLower() == normalizedEmail,
            cancellationToken);

        if (exists)
        {
            return Conflict("User with the same username or email already exists.");
        }

        var user = new User
        {
            Username = normalizedUsername,
            Email = normalizedEmail,
            CreatedAt = DateTimeOffset.UtcNow
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(_jwtTokenService.CreateAuthResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var login = request.Login.Trim();
        var loginLower = login.ToLowerInvariant();

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            item => item.Username.ToLower() == loginLower || item.Email.ToLower() == loginLower,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized("Invalid login or password.");
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized("Invalid login or password.");
        }

        return Ok(_jwtTokenService.CreateAuthResponse(user));
    }
}
