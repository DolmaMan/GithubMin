namespace GithubMinClient.Services;

public class TokenStorageService
{
    public string? AccessToken { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    public void SetToken(string accessToken) => AccessToken = accessToken;
    public void Clear() => AccessToken = null;
}
