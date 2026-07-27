using Microsoft.Extensions.Logging;
using SpotifyBooster.Infrastructure.Logging;

namespace SpotifyBooster.Core.Tests;

public sealed class AsyncFileLoggerProviderTests : IDisposable
{
    private const long MaximumLogFileBytes = 512;
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SpotifyBooster.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void LongRunningWriter_RotatesWithoutExceedingMaximumFileSize()
    {
        using (var provider = new AsyncFileLoggerProvider(
            temporaryDirectory,
            MaximumLogFileBytes,
            queueCapacity: 256))
        {
            ILogger logger = provider.CreateLogger("RotationTest");
            for (int index = 0; index < 100; index++)
            {
                logger.LogInformation("Entry {Index}: {Payload}", index, new string('x', 48));
            }
        }

        string currentPath = Path.Combine(temporaryDirectory, "spotify-booster.log");
        string previousPath = currentPath + ".previous";
        Assert.True(File.Exists(currentPath));
        Assert.True(File.Exists(previousPath));
        Assert.InRange(new FileInfo(currentPath).Length, 1, MaximumLogFileBytes);
        Assert.InRange(new FileInfo(previousPath).Length, 1, MaximumLogFileBytes);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
