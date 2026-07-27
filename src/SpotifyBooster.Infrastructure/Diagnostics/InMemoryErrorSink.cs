using System.Collections.Concurrent;
using SpotifyBooster.Core.Abstractions;
using SpotifyBooster.Core.Models;

namespace SpotifyBooster.Infrastructure.Diagnostics;

public sealed class InMemoryErrorSink : IErrorSink
{
    private const int Capacity = 100;
    private readonly ConcurrentQueue<DiagnosticError> errors = new();

    public void Report(string component, string message)
    {
        string safeComponent = Sanitize(component, 64);
        string safeMessage = Sanitize(RedactKnownPaths(message), 512);
        errors.Enqueue(new DiagnosticError(DateTimeOffset.UtcNow, safeComponent, safeMessage));

        while (errors.Count > Capacity)
        {
            errors.TryDequeue(out _);
        }
    }

    public IReadOnlyList<DiagnosticError> GetRecent(int maximumCount = 20)
    {
        int safeMaximum = Math.Clamp(maximumCount, 1, Capacity);
        return errors.Reverse().Take(safeMaximum).ToArray();
    }

    internal static string RedactKnownPaths(string value)
    {
        string result = value;
        foreach ((Environment.SpecialFolder folder, string token) in new[]
        {
            (Environment.SpecialFolder.UserProfile, "%USERPROFILE%"),
            (Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
            (Environment.SpecialFolder.ApplicationData, "%APPDATA%"),
        })
        {
            string path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path))
            {
                result = result.Replace(path, token, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private static string Sanitize(string value, int maximumLength)
    {
        string singleLine = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return singleLine.Length <= maximumLength
            ? singleLine
            : singleLine[..maximumLength];
    }
}
