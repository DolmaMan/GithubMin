using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Models;
using GithubMinServer.Services;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;
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
        var validationError = ValidateRegistration(normalizedUsername, normalizedEmail, request.Password);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var usernameExists = await _dbContext.Users.AnyAsync(
            user => user.Username.ToLower() == normalizedUsername.ToLower(),
            cancellationToken);

        if (usernameExists)
        {
            return Conflict("Пользователь с таким именем уже существует.");
        }

        var emailExists = await _dbContext.Users.AnyAsync(
            user => user.Email.ToLower() == normalizedEmail,
            cancellationToken);

        if (emailExists)
        {
            return Conflict("Пользователь с такой электронной почтой уже существует.");
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
        var validationError = ValidateLogin(login, request.Password);
        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            item => item.Username.ToLower() == loginLower || item.Email.ToLower() == loginLower,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized("Неверный логин или пароль.");
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized("Неверный логин или пароль.");
        }

        return Ok(_jwtTokenService.CreateAuthResponse(user));
    }

    private static string? ValidateRegistration(string username, string email, string password)
    {
        var errors = new StringBuilder();

        if (string.IsNullOrWhiteSpace(username))
        {
            errors.AppendLine("Введите имя пользователя.");
        }
        else
        {
            if (username.Length < 3 || username.Length > 50)
            {
                errors.AppendLine("Имя пользователя должно содержать от 3 до 50 символов.");
            }

            if (!Regex.IsMatch(username, "^[A-Za-zА-Яа-я0-9_-]+$"))
            {
                errors.AppendLine("Имя пользователя может содержать только буквы, цифры, дефис и нижнее подчеркивание.");
            }
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors.AppendLine("Введите электронную почту.");
        }
        else if (!new EmailAddressAttribute().IsValid(email))
        {
            errors.AppendLine("Введите корректную электронную почту.");
        }

        AppendPasswordErrors(password, errors);
        return errors.Length == 0 ? null : errors.ToString().Trim();
    }

    private static string? ValidateLogin(string login, string password)
    {
        var errors = new StringBuilder();

        if (string.IsNullOrWhiteSpace(login))
        {
            errors.AppendLine("Введите логин или электронную почту.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.AppendLine("Введите пароль.");
        }

        return errors.Length == 0 ? null : errors.ToString().Trim();
    }

    private static void AppendPasswordErrors(string password, StringBuilder errors)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errors.AppendLine("Введите пароль.");
            return;
        }

        if (password.Length < 8 || password.Length > 100)
        {
            errors.AppendLine("Пароль должен содержать от 8 до 100 символов.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.AppendLine("Пароль должен содержать хотя бы одну строчную букву.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.AppendLine("Пароль должен содержать хотя бы одну заглавную букву.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.AppendLine("Пароль должен содержать хотя бы одну цифру.");
        }
    }
}
