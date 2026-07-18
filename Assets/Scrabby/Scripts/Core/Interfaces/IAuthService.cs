using System.Threading.Tasks;

public interface IAuthService
{
    bool IsSignedIn { get; }
    string CurrentUserId { get; }
    string CurrentDisplayName { get; }

    Task InitializeAsync();
    Task<string> SignInAnonymouslyAsync();
    Task SignOutAsync();
}
