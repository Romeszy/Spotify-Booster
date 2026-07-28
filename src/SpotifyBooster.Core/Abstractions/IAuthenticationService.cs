using SpotifyBooster.Contracts.Auth;

namespace SpotifyBooster.Core.Abstractions;

public interface IAuthenticationService
{
    Task<AuthSessionContract?> SignUpAsync(
        SignUpRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthSessionContract> SignInAsync(
        SignInRequest request,
        CancellationToken cancellationToken = default);

    Task<SignOutResult> SignOutAsync(CancellationToken cancellationToken = default);

    Task<AuthSessionContract> RefreshSessionAsync(
        CancellationToken cancellationToken = default);

    Task ResendVerificationAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task RequestPasswordResetAsync(
        PasswordResetRequest request,
        CancellationToken cancellationToken = default);

    Task UpdatePasswordAsync(
        UpdatePasswordRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<AuthSessionContract?> GetSessionAsync(
        CancellationToken cancellationToken = default);
}
