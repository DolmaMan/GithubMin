using GithubMinClient.Models;

namespace GithubMinClient.ViewModels;

public class UserSearchItemViewModel(PublicUserResponse user)
{
    public PublicUserResponse User { get; } = user;

    public Guid Id => User.Id;
    public string Username => User.Username;
    public DateTimeOffset CreatedAt => User.CreatedAt;
}
