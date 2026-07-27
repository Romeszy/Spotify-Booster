using SpotifyBooster.Infrastructure.Windows;

namespace SpotifyBooster.Core.Tests;

public sealed class StartupServiceTests
{
    [Fact]
    public void BuildStartupCommand_QuotesAbsolutePathAndAddsMinimizedSwitch()
    {
        string command = StartupService.BuildStartupCommand(
            @"C:\Users\Test User\Spotify Booster\SpotifyBooster.App.exe");

        Assert.Equal(
            "\"C:\\Users\\Test User\\Spotify Booster\\SpotifyBooster.App.exe\" --minimized",
            command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SpotifyBooster.App.exe")]
    [InlineData(@"C:\Spotify Booster\SpotifyBooster.App.dll")]
    [InlineData("C:\\Spotify Booster\\bad\"name.exe")]
    [InlineData("C:\\Spotify Booster\\bad\rname.exe")]
    [InlineData("C:\\Spotify Booster\\bad\nname.exe")]
    public void BuildStartupCommand_RejectsUnsafeOrUnsupportedPath(string path)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => StartupService.BuildStartupCommand(path));
    }
}
