using System.ComponentModel.DataAnnotations;

namespace GithubMinServer.Contracts;

public class RegisterRequest
{
    [Required(ErrorMessage = "Введите имя пользователя.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Имя пользователя должно содержать от 3 до 50 символов.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите электронную почту.")]
    [EmailAddress(ErrorMessage = "Введите корректную электронную почту.")]
    [StringLength(100, ErrorMessage = "Электронная почта не должна быть длиннее 100 символов.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите пароль.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Пароль должен содержать от 8 до 100 символов.")]
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required(ErrorMessage = "Введите логин или электронную почту.")]
    [StringLength(100, ErrorMessage = "Логин не должен быть длиннее 100 символов.")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Введите пароль.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Пароль должен содержать от 8 до 100 символов.")]
    public string Password { get; set; } = string.Empty;
}

public record UserResponse(Guid Id, string Username, string Email, DateTimeOffset CreatedAt);

public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);
