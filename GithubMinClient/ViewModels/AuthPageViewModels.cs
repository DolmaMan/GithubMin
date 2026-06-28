using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubMinClient.Models;
using GithubMinClient.Services;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.RegularExpressions;

namespace GithubMinClient.ViewModels;

public partial class LoginPageViewModel : ObservableObject
{
    private readonly MainViewModel main;

    public LoginPageViewModel(MainViewModel main)
    {
        this.main = main;
        Login = main.TokenStorageService.LastLogin;
    }

    [ObservableProperty]
    private string login = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    private async Task LoginAsync()
    {
        await RunSafelyAsync(async () =>
        {
            var validationError = AuthInputValidator.ValidateLogin(Login, Password);
            if (validationError is not null)
            {
                StatusMessage = validationError;
                return;
            }

            var response = await main.AuthService.LoginAsync(new LoginRequest { Login = Login.Trim(), Password = Password });
            main.TokenStorageService.SetSession(response);
            await main.ShowProjectsAsync();
        });
    }

    [RelayCommand]
    private void OpenRegister() => main.ShowRegister();

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await action();
        }
        catch (ApiException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = UserMessageTranslator.GetMessageException(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class RegisterPageViewModel : ObservableObject
{
    private readonly MainViewModel main;

    public RegisterPageViewModel(MainViewModel main)
    {
        this.main = main;
    }

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string email = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    private async Task RegisterAsync()
    {
        await RunSafelyAsync(async () =>
        {
            var validationError = AuthInputValidator.ValidateRegistration(Username, Email, Password);
            if (validationError is not null)
            {
                StatusMessage = validationError;
                return;
            }

            var response = await main.AuthService.RegisterAsync(new RegisterRequest
            {
                Username = Username.Trim(),
                Email = Email.Trim(),
                Password = Password
            });

            main.TokenStorageService.SetSession(response);
            await main.ShowProjectsAsync();
        });
    }

    [RelayCommand]
    private void OpenLogin() => main.OpenLoginPage();

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await action();
        }
        catch (ApiException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = UserMessageTranslator.GetMessageException(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

internal static class AuthInputValidator
{
    public static string? ValidateLogin(string login, string password)
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

    public static string? ValidateRegistration(string username, string email, string password)
    {
        var errors = new StringBuilder();

        if (string.IsNullOrWhiteSpace(username))
        {
            errors.AppendLine("Введите имя пользователя.");
        }
        else
        {
            var trimmedUsername = username.Trim();
            if (trimmedUsername.Length < 3 || trimmedUsername.Length > 50)
            {
                errors.AppendLine("Имя пользователя должно содержать от 3 до 50 символов.");
            }

            if (!Regex.IsMatch(trimmedUsername, "^[A-Za-zА-Яа-я0-9_-]+$"))
            {
                errors.AppendLine("Имя пользователя может содержать только буквы, цифры, дефис и нижнее подчеркивание.");
            }
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            errors.AppendLine("Введите электронную почту.");
        }
        else if (!new EmailAddressAttribute().IsValid(email.Trim()))
        {
            errors.AppendLine("Введите корректную электронную почту.");
        }

        AppendPasswordErrors(password, errors);
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
