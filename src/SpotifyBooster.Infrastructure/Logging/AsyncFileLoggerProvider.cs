using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace SpotifyBooster.Infrastructure.Logging;

public sealed class AsyncFileLoggerProvider : ILoggerProvider
{
    private const long DefaultMaximumLogFileBytes = 2 * 1_024 * 1_024;
    private const int DefaultQueueCapacity = 1_024;
    private const int MaximumMessageCharacters = 32_768;
    private readonly Channel<LogEntry> channel;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task writerTask;
    private readonly string logDirectory;
    private readonly long maximumLogFileBytes;
    private long droppedEntries;
    private long pendingDroppedEntries;
    private int disposed;

    public AsyncFileLoggerProvider()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpotifyBooster",
                "logs"),
            DefaultMaximumLogFileBytes,
            DefaultQueueCapacity)
    {
    }

    public AsyncFileLoggerProvider(
        string logDirectory,
        long maximumLogFileBytes,
        int queueCapacity = DefaultQueueCapacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLogFileBytes, 256);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumLogFileBytes, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(queueCapacity, 1);

        this.logDirectory = Path.GetFullPath(logDirectory);
        this.maximumLogFileBytes = maximumLogFileBytes;
        channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        writerTask = Task.Run(WriteLoopAsync);
    }

    public long DroppedEntryCount => Interlocked.Read(ref droppedEntries);

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref disposed) != 0,
            this);
        return new AsyncFileLogger(this, categoryName);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        channel.Writer.TryComplete();
        try
        {
            if (!writerTask.Wait(TimeSpan.FromSeconds(2)))
            {
                cancellation.Cancel();
                writerTask.Wait(TimeSpan.FromSeconds(1));
            }
        }
        catch (AggregateException)
        {
        }
        finally
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    internal bool TryWrite(
        LogLevel level,
        string category,
        string message)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return false;
        }

        string safeMessage = Diagnostics.InMemoryErrorSink.RedactKnownPaths(message);
        bool accepted = channel.Writer.TryWrite(new(
            DateTimeOffset.UtcNow,
            level,
            category,
            safeMessage[..Math.Min(
                safeMessage.Length,
                MaximumMessageCharacters)]));
        if (!accepted)
        {
            Interlocked.Increment(ref droppedEntries);
            Interlocked.Increment(ref pendingDroppedEntries);
        }

        return accepted;
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "spotify-booster.log");
            StreamWriter? writer = null;
            try
            {
                writer = await OpenWriterAsync(logPath).ConfigureAwait(false);
                await foreach (LogEntry entry in channel.Reader.ReadAllAsync(cancellation.Token))
                {
                    long dropped = Interlocked.Exchange(ref pendingDroppedEntries, 0);
                    if (dropped > 0)
                    {
                        writer = await WriteLineWithRotationAsync(
                            writer,
                            logPath,
                            $"{DateTimeOffset.UtcNow:O} [Warning] SpotifyBooster.Infrastructure.Logging: {dropped} log entr{(dropped == 1 ? "y was" : "ies were")} dropped because the logging queue was full.")
                            .ConfigureAwait(false);
                    }

                    string safeMessage = entry.Message
                        .Replace('\r', ' ')
                        .Replace('\n', ' ');
                    writer = await WriteLineWithRotationAsync(
                        writer,
                        logPath,
                        $"{entry.Timestamp:O} [{entry.Level}] {entry.Category}: {safeMessage}")
                        .ConfigureAwait(false);
                }

                long finalDropped = Interlocked.Exchange(ref pendingDroppedEntries, 0);
                if (finalDropped > 0)
                {
                    writer = await WriteLineWithRotationAsync(
                        writer,
                        logPath,
                        $"{DateTimeOffset.UtcNow:O} [Warning] SpotifyBooster.Infrastructure.Logging: {finalDropped} queued log entries were dropped.")
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                if (writer is not null)
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Spotify Booster file logging stopped: {exception.GetType().Name}");
        }
    }

    private async Task<StreamWriter> WriteLineWithRotationAsync(
        StreamWriter writer,
        string logPath,
        string line)
    {
        string boundedLine = TruncateToUtf8ByteCount(
            line,
            checked((int)maximumLogFileBytes) - Encoding.UTF8.GetByteCount(Environment.NewLine));
        int bytesToWrite = Encoding.UTF8.GetByteCount(boundedLine)
            + Encoding.UTF8.GetByteCount(Environment.NewLine);
        if (writer.BaseStream.Position + bytesToWrite > maximumLogFileBytes)
        {
            await writer.DisposeAsync().ConfigureAwait(false);
            Rotate(logPath);
            writer = await OpenWriterAsync(logPath).ConfigureAwait(false);
        }

        await writer.WriteLineAsync(boundedLine).ConfigureAwait(false);
        return writer;
    }

    private async Task<StreamWriter> OpenWriterAsync(string logPath)
    {
        if (File.Exists(logPath)
            && new FileInfo(logPath).Length >= maximumLogFileBytes)
        {
            Rotate(logPath);
        }

        var stream = new FileStream(
            logPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        await writer.FlushAsync(cancellation.Token).ConfigureAwait(false);
        return writer;
    }

    private static void Rotate(string logPath)
    {
        string previousPath = logPath + ".previous";
        File.Move(logPath, previousPath, overwrite: true);
    }

    private static string TruncateToUtf8ByteCount(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }

        const string suffix = " [truncated]";
        int availableBytes = maximumBytes - Encoding.UTF8.GetByteCount(suffix);
        int low = 0;
        int high = value.Length;
        while (low < high)
        {
            int candidate = low + ((high - low + 1) / 2);
            if (Encoding.UTF8.GetByteCount(value.AsSpan(0, candidate)) <= availableBytes)
            {
                low = candidate;
            }
            else
            {
                high = candidate - 1;
            }
        }

        return string.Concat(value.AsSpan(0, low), suffix);
    }

    private sealed record LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Category,
        string Message);

    private sealed class AsyncFileLogger : ILogger
    {
        private readonly AsyncFileLoggerProvider provider;
        private readonly string category;

        public AsyncFileLogger(
            AsyncFileLoggerProvider provider,
            string category)
        {
            this.provider = provider;
            this.category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message} ({exception.GetType().Name}: {exception.Message})";
            }

            provider.TryWrite(logLevel, category, message);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
