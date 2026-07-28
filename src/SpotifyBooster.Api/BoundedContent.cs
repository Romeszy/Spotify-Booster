namespace SpotifyBooster.Api;

internal static class BoundedContent
{
    public static async Task<byte[]> ReadAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("Response exceeds the configured limit.");
        await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 4096));
        var buffer = new byte[4096];
        while (true)
        {
            var count = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes + 1 - (int)destination.Length)), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0) break;
            destination.Write(buffer, 0, count);
            if (destination.Length > maximumBytes)
                throw new InvalidDataException("Response exceeds the configured limit.");
        }
        return destination.ToArray();
    }
}
