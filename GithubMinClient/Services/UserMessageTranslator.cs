using System.IO;
using System.Net.Http;

namespace GithubMinClient.Services;

public static class UserMessageTranslator
{
    public static string GetMessageException(Exception exception)
    {
        return exception switch
        {
            HttpRequestException => "Не удалось подключиться к серверу. Проверьте, что сервер запущен и доступен.",
            TaskCanceledException => "Операция заняла слишком много времени или была отменена.",
            UnauthorizedAccessException => "Нет доступа к выбранным файлам или папке.",
            DirectoryNotFoundException => "Рабочая директория не найдена.",
            FileNotFoundException => "Файл не найден.",
            IOException => "Произошла ошибка при работе с файлами. Проверьте доступ к рабочей директории.",
            InvalidOperationException => GetSafeMessage(exception.Message),
            _ => string.IsNullOrWhiteSpace(exception.Message)
                ? "Произошла непредвиденная ошибка."
                : GetSafeMessage(exception.Message)
        };
    }

    private static string GetSafeMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Произошла непредвиденная ошибка.";
        }

        return message.Any(character => character >= 'А' && character <= 'я')
            ? message
            : "Произошла непредвиденная ошибка.";
    }
}
