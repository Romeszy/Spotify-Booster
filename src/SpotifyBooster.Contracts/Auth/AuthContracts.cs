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

public sealed record SignUpRequest(string Email, [property: JsonIgnore] string Password)
{
    public override string ToString() => $"{nameof(SignUpRequest)} {{ Email = {Email}, Password = [REDACTED] }}";
}
public sealed record SignInRequest(string Email, [property: JsonIgnore] string Password)
{
    public override string ToString() => $"{nameof(SignInRequest)} {{ Email = {Email}, Password = [REDACTED] }}";
}
public sealed record PasswordResetRequest(string Email);
public sealed record UpdatePasswordRequest([property: JsonIgnore] string Password)
{
    public override string ToString() => $"{nameof(UpdatePasswordRequest)} {{ Password = [REDACTED] }}";
}

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
