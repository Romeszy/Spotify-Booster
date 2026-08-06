using System.Text.Json.Serialization;

namespace SpotifyBooster.Contracts.Auth;

public sealed record AuthUserContract(Guid Id, string Email, bool EmailVerified)
{
    public override string ToString() =>
        $"{nameof(AuthUserContract)} {{ Id = [REDACTED], Email = [REDACTED], EmailVerified = {EmailVerified} }}";
}

public enum AuthSessionPurpose
{
    Standard,
    PasswordRecovery,
}

public sealed record AuthSessionContract(
    AuthUserContract User,
    DateTimeOffset AccessTokenExpiresAt,
    [property: JsonIgnore] string AccessToken,
    [property: JsonIgnore] string RefreshToken,
    AuthSessionPurpose Purpose = AuthSessionPurpose.Standard)
{
    public override string ToString() =>
        $"{nameof(AuthSessionContract)} {{ User = [REDACTED], AccessTokenExpiresAt = {AccessTokenExpiresAt:O}, Purpose = {Purpose} }}";
}

public sealed record SignUpRequest(string Email, [property: JsonIgnore] string Password)
{
    public override string ToString() =>
        $"{nameof(SignUpRequest)} {{ Email = [REDACTED], Password = [REDACTED] }}";
}
public sealed record SignInRequest(string Email, [property: JsonIgnore] string Password)
{
    public override string ToString() =>
        $"{nameof(SignInRequest)} {{ Email = [REDACTED], Password = [REDACTED] }}";
}
public sealed record PasswordResetRequest(string Email)
{
    public override string ToString() =>
        $"{nameof(PasswordResetRequest)} {{ Email = [REDACTED] }}";
}
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

public enum LocalSignOut
{
    Completed,
    Failed,
}

public sealed record SignOutResult(
    RemoteRevocation RemoteRevocation,
    LocalSignOut LocalSignOut = LocalSignOut.Completed);
