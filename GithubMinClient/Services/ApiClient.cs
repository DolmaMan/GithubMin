using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GithubMinClient.Services;

public class ApiClient(TokenStorageService tokenStorageService)
{
    private readonly Uri[] _baseAddresses =
    [
        new("https://localhost:7051/"),
        new("http://localhost:5062/")
    ];

    private int _preferredBaseAddressIndex;

    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            request.RequestUri?.IsLoopback == true || errors == SslPolicyErrors.None
    })
    {
        Timeout = TimeSpan.FromMinutes(60)
    };

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<T> GetAsync<T>(string url, CancellationToken cancellationToken = default) =>
        SendAsync<T>(baseAddress => new HttpRequestMessage(HttpMethod.Get, CreateRequestUri(baseAddress, url)), cancellationToken);

    public Task<T> PostAsync<T>(string url, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(baseAddress => new HttpRequestMessage(HttpMethod.Post, CreateRequestUri(baseAddress, url))
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        }, cancellationToken);

    public Task<T> PutAsync<T>(string url, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(baseAddress => new HttpRequestMessage(HttpMethod.Put, CreateRequestUri(baseAddress, url))
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        }, cancellationToken);

    public async Task<T> PostMultipartAsync<T>(
        string url,
        string message,
        Guid? branchId,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendAsync<T>(baseAddress =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, CreateRequestUri(baseAddress, url));
                var content = new MultipartFormDataContent();
                content.Add(new StringContent(message), "Message");

                if (branchId.HasValue)
                {
                    content.Add(new StringContent(branchId.Value.ToString()), "BranchId");
                }

                var stream = File.OpenRead(archivePath);
                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                content.Add(fileContent, "Archive", Path.GetFileName(archivePath));
                request.Content = content;
                return request;
            }, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new HttpRequestException(
                "Не удалось отправить архив коммита на сервер. Проверьте, что сервер запущен, а размер архива не превышает лимит.",
                exception);
        }
    }

    public async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        await SendAsync(
            baseAddress => new HttpRequestMessage(HttpMethod.Get, CreateRequestUri(baseAddress, url)),
            async response =>
            {
                await EnsureSuccessAsync(response, cancellationToken);

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = File.Create(destinationPath);
                await input.CopyToAsync(output, cancellationToken);
                return true;
            },
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);
    }

    private Task<T> SendAsync<T>(Func<Uri, HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        return SendAsync(
            requestFactory,
            response => ReadResponseAsync<T>(response, cancellationToken),
            cancellationToken,
            HttpCompletionOption.ResponseContentRead);
    }

    private async Task<T> SendAsync<T>(
        Func<Uri, HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, Task<T>> responseHandler,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption)
    {
        Exception? lastException = null;

        foreach (var baseAddress in GetOrderedBaseAddresses())
        {
            try
            {
                using var request = requestFactory(baseAddress);
                ApplyAuthorization(request);
                using var response = await _httpClient.SendAsync(request, completionOption, cancellationToken);
                RememberSuccessfulBaseAddress(baseAddress);
                return await responseHandler(response);
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
            }
        }

        throw lastException ?? new HttpRequestException("Не удалось подключиться к серверу.");
    }

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        return result ?? throw new ApiException(response.StatusCode, "Сервер вернул пустой ответ.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new ApiException(response.StatusCode, ToReadableError(message, response.StatusCode));
    }

    private static string ToReadableError(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return GetStatusError(statusCode);
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            var builder = new StringBuilder();

            if (root.TryGetProperty("message", out var messageProperty))
            {
                builder.Append(messageProperty.GetString());
            }
            else if (root.TryGetProperty("title", out var titleProperty))
            {
                builder.Append(titleProperty.GetString());
            }

            if (root.TryGetProperty("conflicts", out var conflictsProperty) && conflictsProperty.ValueKind == JsonValueKind.Array)
            {
                var conflicts = conflictsProperty.EnumerateArray()
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();

                if (conflicts.Length > 0)
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append("Конфликты: ");
                    builder.Append(string.Join(", ", conflicts));
                }
            }

            if (root.TryGetProperty("errors", out var errorsProperty) && errorsProperty.ValueKind == JsonValueKind.Object)
            {
                foreach (var error in errorsProperty.EnumerateObject())
                {
                    foreach (var value in error.Value.EnumerateArray())
                    {
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        builder.Append(value.GetString());
                    }
                }
            }

            if (builder.Length > 0)
            {
                return builder.ToString();
            }
        }
        catch
        {
            // If the server returned plain text, use it below.
        }

        var trimmed = responseBody.Trim('"');
        return string.IsNullOrWhiteSpace(trimmed) ? GetStatusError(statusCode) : trimmed;
    }

    private static string GetStatusError(System.Net.HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            System.Net.HttpStatusCode.BadRequest => "Некорректный запрос.",
            System.Net.HttpStatusCode.Unauthorized => "Не выполнен вход в систему.",
            System.Net.HttpStatusCode.Forbidden => "Недостаточно прав для выполнения операции.",
            System.Net.HttpStatusCode.NotFound => "Запрошенные данные не найдены.",
            System.Net.HttpStatusCode.Conflict => "Операция не может быть выполнена из-за конфликта данных.",
            System.Net.HttpStatusCode.RequestEntityTooLarge => "Архив слишком большой. Уменьшите размер рабочей директории или исключите лишние файлы.",
            System.Net.HttpStatusCode.InternalServerError => "На сервере произошла ошибка.",
            _ => "Ошибка при обращении к серверу."
        };
    }

    private void ApplyAuthorization(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(tokenStorageService.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStorageService.AccessToken);
        }
    }

    private Uri CreateRequestUri(Uri baseAddress, string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(baseAddress, url);

    private IEnumerable<Uri> GetOrderedBaseAddresses()
    {
        yield return _baseAddresses[_preferredBaseAddressIndex];

        for (var index = 0; index < _baseAddresses.Length; index++)
        {
            if (index != _preferredBaseAddressIndex)
            {
                yield return _baseAddresses[index];
            }
        }
    }

    private void RememberSuccessfulBaseAddress(Uri baseAddress)
    {
        var index = Array.IndexOf(_baseAddresses, baseAddress);
        if (index >= 0)
        {
            _preferredBaseAddressIndex = index;
        }
    }
}
