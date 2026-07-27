using NAudio.Wave;

namespace SpotifyBooster.Audio.Pipeline;

internal sealed class StandardWaveFormatProvider : IWaveProvider
{
    private readonly IWaveProvider source;

    public StandardWaveFormatProvider(IWaveProvider source)
    {
        this.source = source;
        WaveFormat = source.WaveFormat.AsStandardWaveFormat();
        if (WaveFormat.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat))
        {
            throw new NotSupportedException(
                $"Le format de capture {source.WaveFormat.Encoding} n’est pas pris en charge.");
        }
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        return source.Read(buffer, offset, count);
    }
}
