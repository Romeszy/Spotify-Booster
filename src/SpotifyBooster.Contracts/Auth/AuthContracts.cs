using System.Text.Json.Serialization;

namespace SpotifyBooster.Contracts.Auth;

public sealed record AuthUserContract(Guid Id, string Email, bool EmailVerified);

public sealed record AuthSessionContract(
    AuthUserContract User,
    DateTimeOffset AccessTokenExpiresAt,
    [property: JsonIgnore] string AccessToken,
    [property: JsonIgnore] string RefreshToken)
{
    public override string ToString() =>
        $"{nameof(AuthSessionContract)} {{ User = {User.Id}, AccessTokenExpiresAt = {AccessTokenExpiresAt:O} }}";
}

public sealed record SignUpRequest(string Email, [property: JsonIgnore] string Password);
public sealed record SignInRequest(string Email, [property: JsonIgnore] string Password);
public sealed record PasswordResetRequest(string Email);
public sealed record UpdatePasswordRequest([property: JsonIgnore] string Password);

public enum RemoteRevocation
{
    NotAttempted,
    Succeeded,
    Failed
}

public enum LocalSignOut { Completed }

public sealed record SignOutResult(
    RemoteRevocation RemoteRevocation,
    LocalSignOut LocalSignOut = LocalSignOut.Completed);

public interface IAuthenticationService
{
    Task<AuthSessionContract?> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken = default);
    Task<AuthSessionContract> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);
    Task<SignOutResult> SignOutAsync(CancellationToken cancellationToken = default);
    Task<AuthSessionContract> RefreshSessionAsync(CancellationToken cancellationToken = default);
    Task ResendVerificationAsync(string email, CancellationToken cancellationToken = default);
    Task RequestPasswordResetAsync(PasswordResetRequest request, CancellationToken cancellationToken = default);
    Task UpdatePasswordAsync(UpdatePasswordRequest request, CancellationToken cancellationToken = default);
    ValueTask<AuthSessionContract?> GetSessionAsync(CancellationToken cancellationToken = default);
}
