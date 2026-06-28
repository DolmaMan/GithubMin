using System.IO;
using System.Text.Json;
using GithubMinClient.Models;

namespace GithubMinClient.Services;

public class TokenStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _storageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubMinClient");

    private readonly string _sessionPath;
    private readonly string _legacyTokenPath;
    private SavedSession _session;

    public TokenStorageService()
    {
        _sessionPath = Path.Combine(_storageDirectory, "session.json");
        _legacyTokenPath = Path.Combine(_storageDirectory, "token.txt");
        _session = LoadSession();
    }

    public string? AccessToken => _session.AccessToken;
    public Guid? UserId => _session.UserId;
    public string? Username => _session.Username;
    public string? Email => _session.Email;
    public DateTimeOffset? ExpiresAt => _session.ExpiresAt;
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);
    public bool HasActiveSession => IsAuthenticated && (!ExpiresAt.HasValue || ExpiresAt.Value > DateTimeOffset.UtcNow);
    public bool IsSessionExpired => IsAuthenticated && ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;
    public string LastLogin =>
        !string.IsNullOrWhiteSpace(Username)
            ? Username
            : Email ?? string.Empty;

    public void SetSession(AuthResponse response)
    {
        _session = new SavedSession
        {
            AccessToken = response.AccessToken,
            UserId = response.User.Id,
            Username = response.User.Username,
            Email = response.User.Email,
            ExpiresAt = response.ExpiresAt
        };

        PersistSession();
    }

    public bool IsCurrentUser(Guid userId, string username)
    {
        if (_session.UserId.HasValue)
        {
            return _session.UserId.Value == userId;
        }

        return !string.IsNullOrWhiteSpace(username) &&
            string.Equals(username, _session.Username, StringComparison.OrdinalIgnoreCase);
    }

    public void ClearAuthentication(bool preserveLastAccount = true)
    {
        if (preserveLastAccount)
        {
            _session = new SavedSession
            {
                Username = _session.Username,
                Email = _session.Email
            };
        }
        else
        {
            _session = new SavedSession();
        }

        PersistSession();
    }

    private SavedSession LoadSession()
    {
        if (File.Exists(_sessionPath))
        {
            try
            {
                var json = File.ReadAllText(_sessionPath);
                var session = JsonSerializer.Deserialize<SavedSession>(json, JsonOptions);
                if (session is not null)
                {
                    return session;
                }
            }
            catch
            {
                // Ignore broken persisted session and fall back to legacy storage.
            }
        }

        if (File.Exists(_legacyTokenPath))
        {
            var token = File.ReadAllText(_legacyTokenPath).Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return new SavedSession { AccessToken = token };
            }
        }

        return new SavedSession();
    }

    private void PersistSession()
    {
        Directory.CreateDirectory(_storageDirectory);

        if (string.IsNullOrWhiteSpace(_session.AccessToken) &&
            string.IsNullOrWhiteSpace(_session.Username) &&
            string.IsNullOrWhiteSpace(_session.Email))
        {
            if (File.Exists(_sessionPath))
            {
                File.Delete(_sessionPath);
            }

            if (File.Exists(_legacyTokenPath))
            {
                File.Delete(_legacyTokenPath);
            }

            return;
        }

        var json = JsonSerializer.Serialize(_session, JsonOptions);
        File.WriteAllText(_sessionPath, json);

        if (File.Exists(_legacyTokenPath))
        {
            File.Delete(_legacyTokenPath);
        }
    }

    private sealed class SavedSession
    {
        public string? AccessToken { get; set; }
        public Guid? UserId { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
