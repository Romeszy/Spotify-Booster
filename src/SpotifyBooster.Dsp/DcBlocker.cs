namespace SpotifyBooster.Dsp;

public sealed class DcBlocker
{
    private readonly float _pole;
    private float _previousInputLeft;
    private float _previousInputRight;
    private float _previousOutputLeft;
    private float _previousOutputRight;

    public DcBlocker(int sampleRate, float cutoffHz = 10f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        cutoffHz = Math.Clamp(cutoffHz, 1f, sampleRate * 0.1f);
        _pole = MathF.Exp(-2f * MathF.PI * cutoffHz / sampleRate);
    }

    public void Process(ref float left, ref float right)
    {
        float outputLeft = left - _previousInputLeft + (_pole * _previousOutputLeft);
        float outputRight = right - _previousInputRight + (_pole * _previousOutputRight);
        _previousInputLeft = left;
        _previousInputRight = right;
        _previousOutputLeft = outputLeft;
        _previousOutputRight = outputRight;
        left = outputLeft;
        right = outputRight;
    }

    public void Reset()
    {
        ResetLeft();
        ResetRight();
    }

    public void ResetLeft()
    {
        _previousInputLeft = 0f;
        _previousOutputLeft = 0f;
    }

    public void ResetRight()
    {
        _previousInputRight = 0f;
        _previousOutputRight = 0f;
    }
}
