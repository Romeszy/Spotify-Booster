using NAudio.Wave;

namespace SpotifyBooster.Audio.Pipeline;

internal sealed class MonitoredBufferedWaveProvider : IWaveProvider
{
    private readonly BufferedWaveProvider inner;
    private long underrunCount;
    private long overrunCount;

    public MonitoredBufferedWaveProvider(
        WaveFormat waveFormat,
        TimeSpan capacity)
    {
        inner = new BufferedWaveProvider(waveFormat)
        {
            BufferDuration = capacity,
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
    }

    public WaveFormat WaveFormat => inner.WaveFormat;

    public TimeSpan BufferedDuration => inner.BufferedDuration;

    public long UnderrunCount => Interlocked.Read(ref underrunCount);

    public long OverrunCount => Interlocked.Read(ref overrunCount);

    public void AddSamples(byte[] buffer, int offset, int count)
    {
        if (count > inner.BufferLength - inner.BufferedBytes)
        {
            Interlocked.Increment(ref overrunCount);
        }

        inner.AddSamples(buffer, offset, count);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (inner.BufferedBytes < count)
        {
            Interlocked.Increment(ref underrunCount);
        }

        return inner.Read(buffer, offset, count);
    }

    public void Clear()
    {
        inner.ClearBuffer();
    }
}
