namespace SpotifyBooster.Contracts.Api;

public sealed record ApiErrorContract(string Code, string Message, string TraceId);
public sealed record CurrentUserContract(
    Guid UserId,
    string? DisplayName,
    string Plan,
    string Status,
    long Revision,
    IReadOnlyList<string> Features,
    DateTimeOffset ServerTime);
public sealed record DeleteAccountRequest(string Confirmation);
