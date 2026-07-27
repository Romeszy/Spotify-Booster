using SpotifyBooster.Audio.Engine;

namespace SpotifyBooster.Audio.Tests;

public sealed class ExceptionResilientCleanupTests
{
    [Fact]
    public void Run_AttemptsEveryOperationAndAggregatesFailures()
    {
        var completed = new List<string>();

        AggregateException exception = Assert.Throws<AggregateException>(() =>
            ExceptionResilientCleanup.Run(
                () =>
                {
                    completed.Add("stop-capture");
                    throw new InvalidOperationException("capture");
                },
                () => completed.Add("dispose-output"),
                () =>
                {
                    completed.Add("dispose-capture");
                    throw new ObjectDisposedException("capture");
                },
                () => completed.Add("dispose-endpoints")));

        Assert.Equal(
            ["stop-capture", "dispose-output", "dispose-capture", "dispose-endpoints"],
            completed);
        Assert.Collection(
            exception.InnerExceptions,
            failure => Assert.IsType<InvalidOperationException>(failure),
            failure => Assert.IsType<ObjectDisposedException>(failure));
    }

    [Fact]
    public void Run_PreservesSingleFailureTypeAfterRemainingCleanup()
    {
        bool finalCleanupRan = false;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            ExceptionResilientCleanup.Run(
                () => throw new InvalidOperationException("first"),
                () => finalCleanupRan = true));

        Assert.Equal("first", exception.Message);
        Assert.True(finalCleanupRan);
    }
}
