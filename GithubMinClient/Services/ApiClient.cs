using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GithubMinClient.Services;

public class ApiClient(TokenStorageService tokenStorageService)
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://localhost:5062/")
    };

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<T> GetAsync<T>(string url, CancellationToken cancellationToken = default) =>
        SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);

    public Task<T> PostAsync<T>(string url, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: _jsonOptions) }, cancellationToken);

    public Task<T> PutAsync<T>(string url, object body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body, options: _jsonOptions) }, cancellationToken);

    public async Task<T> PostMultipartAsync<T>(
        string url,
        string message,
        Guid? branchId,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(message), "Message");

        if (branchId.HasValue)
        {
            content.Add(new StringContent(branchId.Value.ToString()), "BranchId");
        }

        await using var stream = File.OpenRead(archivePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "Archive", Path.GetFileName(archivePath));
        request.Content = content;

        ApplyAuthorization(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
    }

    public async Task DownloadAsync(string url, string destinationPath, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuthorization(request);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destinationPath);
        await input.CopyToAsync(output, cancellationToken);
    }

    private async Task<T> SendAsync<T>(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        using var request = requestFactory();
        ApplyAuthorization(request);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadResponseAsync<T>(response, cancellationToken);
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
            return GetStatusError(statusCode);
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
}
