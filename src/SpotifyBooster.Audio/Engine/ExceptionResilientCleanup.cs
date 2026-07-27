using System.Runtime.ExceptionServices;

namespace SpotifyBooster.Audio.Engine;

internal static class ExceptionResilientCleanup
{
    public static void Run(params Action[] operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        List<Exception>? failures = null;
        foreach (Action operation in operations)
        {
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(
            "Une ou plusieurs ressources audio n’ont pas pu être libérées proprement.",
            failures);
    }
}
