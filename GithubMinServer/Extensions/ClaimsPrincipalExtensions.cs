using System.Security.Claims;

namespace GithubMinServer.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new InvalidOperationException("Идентификатор авторизованного пользователя отсутствует.");
    }
}
