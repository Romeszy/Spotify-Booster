namespace SpotifyBooster.Contracts.Api;

public sealed record ApiErrorContract(string Code, string Message, string TraceId);
public sealed record CurrentUserContract(Guid UserId, string Plan, string Status, long Revision);
public sealed record DeleteAccountRequest(string Confirmation);
