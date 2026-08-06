namespace SpotifyBooster.Dsp.Tests;

public sealed class PipelineTests
{
    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    public void PipelineHandlesSilenceAndProtectedMaximumGainAtCommonRates(
        int sampleRate)
    {
        DspSettings settings = TransparentSettings(25f) with
        {
            LimiterEnabled = true,
            LimiterCeilingDecibels = -1f,
        };
        var pipeline = new DspPipeline(sampleRate, settings);
        float[] silence = new float[2_048];

        DspMetrics silenceMetrics = pipeline.Process(silence);
        Assert.All(silence, sample => Assert.Equal(0f, sample));
        Assert.Equal(0f, silenceMetrics.OutputPeak);

        float[] maximum = new float[4_096];
        Array.Fill(maximum, 1f);
        DspMetrics metrics = pipeline.Process(maximum);
        float ceiling = GainConversion.DecibelsToFactor(-1f);

        Assert.All(
            maximum,
            sample => Assert.InRange(Math.Abs(sample), 0f, ceiling + 0.000001f));
        Assert.True(metrics.AfterMainGainPeak > 10f);
        Assert.True(metrics.LimitedFrames > 0);
        Assert.True(metrics.LimiterGainReductionDecibels > 0f);
        Assert.Equal(0, metrics.ClippedSamples);
    }

    [Theory]
    [InlineData(44_100, 20f)]
    [InlineData(44_100, 25f)]
    [InlineData(48_000, 20f)]
    [InlineData(48_000, 25f)]
    public void MinusFortyDbfsSineReceivesExactRawGainBeforeProtections(
        int sampleRate,
        float gainDecibels)
    {
        float[] buffer = CreateStereoSine(
            sampleRate,
            frequencyHz: 997f,
            amplitude: GainConversion.DecibelsToFactor(-40f));
        double inputRms = MeasureRms(buffer);
        var pipeline = new DspPipeline(
            sampleRate,
            TransparentSettings(gainDecibels));

        DspMetrics metrics = pipeline.Process(buffer);

        double measuredGainDecibels =
            20d * Math.Log10(MeasureRms(buffer) / inputRms);
        float expectedPeak = GainConversion.DecibelsToFactor(-40f + gainDecibels);
        Assert.InRange(measuredGainDecibels, gainDecibels - 0.01, gainDecibels + 0.01);
        Assert.InRange(
            metrics.AfterMainGainPeak,
            expectedPeak - 0.0001f,
            expectedPeak + 0.0001f);
        Assert.InRange(
            metrics.OutputPeak,
            expectedPeak - 0.0001f,
            expectedPeak + 0.0001f);
        Assert.Equal(0, metrics.SoftClippedSamples);
        Assert.Equal(0, metrics.LimitedFrames);
        Assert.Equal(0, metrics.ClippedSamples);
    }

    [Fact]
    public void InitialNonNeutralGainUsesConfiguredSmoothing()
    {
        const int sampleRate = 48_000;
        var pipeline = new DspPipeline(
            sampleRate,
            TransparentSettings(25f) with
            {
                GainSmoothingMilliseconds = 20f,
                OutputGainDecibels = 12f,
            });
        float[] buffer = [0.01f, 0.01f];

        DspMetrics metrics = pipeline.Process(buffer);

        Assert.InRange(metrics.AppliedMainGainDecibels, 0.001f, 0.1f);
        Assert.InRange(buffer[0], 0.01f, 0.0103f);
        Assert.Equal(buffer[0], buffer[1]);
    }

    [Theory]
    [InlineData("dc-block")]
    [InlineData("bass")]
    [InlineData("equalizer")]
    [InlineData("compressor")]
    [InlineData("soft-clipper")]
    [InlineData("limiter")]
    public void DisabledOptionalModuleIsTransparent(string module)
    {
        DspSettings settings = module switch
        {
            "dc-block" => TransparentSettings() with
            {
                DcBlockEnabled = false,
            },
            "bass" => TransparentSettings() with
            {
                BassBoostEnabled = false,
                LowShelfFrequencyHz = 35f,
                LowShelfGainDecibels = 18f,
            },
            "equalizer" => TransparentSettings() with
            {
                EqualizerEnabled = false,
                EqualizerLowGainDecibels = 12f,
                EqualizerMidGainDecibels = -12f,
                EqualizerHighGainDecibels = 12f,
            },
            "compressor" => TransparentSettings() with
            {
                CompressorEnabled = false,
                CompressorThresholdDecibels = -60f,
                CompressorRatio = 20f,
                CompressorMakeupGainDecibels = 24f,
            },
            "soft-clipper" => TransparentSettings() with
            {
                SoftClipperEnabled = false,
                SoftClipperThresholdDecibels = -18f,
                SoftClipperDrive = 8f,
            },
            "limiter" => TransparentSettings() with
            {
                LimiterEnabled = false,
                LimiterCeilingDecibels = -12f,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(module)),
        };
        var pipeline = new DspPipeline(48_000, settings);
        float[] original =
        [
            0.25f, -0.5f,
            1.25f, -1.5f,
            -0.75f, 0.125f,
        ];
        float[] buffer = (float[])original.Clone();

        DspMetrics metrics = pipeline.Process(buffer);

        Assert.Equal(original, buffer);
        Assert.Equal(metrics.InputPeak, metrics.OutputPeak);
    }

    [Theory]
    [InlineData(20f)]
    [InlineData(25f)]
    public void LimiterReportsRealPreProtectionGainAndFinalCeiling(
        float gainDecibels)
    {
        const int sampleRate = 48_000;
        float[] buffer = CreateStereoSine(
            sampleRate,
            997f,
            GainConversion.DecibelsToFactor(-10f));
        var pipeline = new DspPipeline(
            sampleRate,
            TransparentSettings(gainDecibels) with
            {
                LimiterEnabled = true,
                LimiterCeilingDecibels = -1f,
            });

        DspMetrics metrics = pipeline.Process(buffer);

        float expectedPreProtectionPeak =
            GainConversion.DecibelsToFactor(-10f + gainDecibels);
        float ceiling = GainConversion.DecibelsToFactor(-1f);
        Assert.InRange(
            metrics.AfterMainGainPeak,
            expectedPreProtectionPeak - 0.001f,
            expectedPreProtectionPeak + 0.001f);
        Assert.True(buffer.Max(Math.Abs) <= ceiling + 0.000001f);
        Assert.True(metrics.AfterLimiterPeak <= ceiling + 0.000001f);
        Assert.True(metrics.OutputPeak <= ceiling + 0.000001f);
        Assert.True(metrics.LimitedFrames > 0);
        Assert.True(metrics.LimiterGainReductionDecibels > 0f);
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    [InlineData(96_000)]
    public void PanicResetUsesFastSmoothingAndReturnsMainAndOutputGainToUnity(
        int sampleRate)
    {
        var pipeline = new DspPipeline(
            sampleRate,
            TransparentSettings(25f) with
            {
                GainSmoothingMilliseconds = 0f,
                PanicSmoothingMilliseconds = 1.5f,
                OutputGainDecibels = 12f,
            });
        float amplitude = GainConversion.DecibelsToFactor(-40f);
        float[] before = CreateStereoSine(sampleRate, 997f, amplitude);
        DspMetrics boosted = pipeline.Process(before);
        Assert.InRange(boosted.AppliedMainGainDecibels, 24.99f, 25.01f);

        pipeline.PanicReset();
        float[] after = CreateStereoSine(sampleRate / 20, 997f, amplitude, sampleRate);
        DspMetrics reset = pipeline.Process(after);

        Assert.InRange(reset.AppliedMainGainDecibels, -0.001f, 0.02f);
        Assert.True(reset.OutputPeak < boosted.OutputPeak);
        double tailRms = MeasureRms(after[^960..]);
        double expectedRms = amplitude / Math.Sqrt(2);
        Assert.InRange(tailRms, expectedRms * 0.99, expectedRms * 1.01);
    }

    [Theory]
    [InlineData(44_100)]
    [InlineData(48_000)]
    [InlineData(96_000)]
    public void PanicGainRampDescendsMonotonicallyAtSupportedRates(
        int sampleRate)
    {
        const float startingGainDecibels = 75f;
        const float panicMilliseconds = 1.5f;
        var pipeline = new DspPipeline(
            sampleRate,
            TransparentSettings(startingGainDecibels) with
            {
                GainSmoothingMilliseconds = 0f,
                PanicSmoothingMilliseconds = panicMilliseconds,
            });
        pipeline.Process([0.001f, 0.001f]);
        pipeline.PanicReset();

        float previous = startingGainDecibels;
        int sampleCount = (int)Math.Ceiling(
            sampleRate * panicMilliseconds / 1000f);
        for (int sample = 0; sample < sampleCount; sample++)
        {
            DspMetrics metrics = pipeline.Process([0.001f, 0.001f]);
            Assert.InRange(metrics.AppliedMainGainDecibels, 0f, previous);
            previous = metrics.AppliedMainGainDecibels;
        }

        Assert.InRange(previous, 0f, 0.0001f);
    }

    [Theory]
    [InlineData("dc-block")]
    [InlineData("bass")]
    [InlineData("equalizer")]
    public void NonFiniteRecoveryPreservesCleanChannelState(string module)
    {
        DspSettings settings = TransparentSettings() with
        {
            DcBlockEnabled = module == "dc-block",
            BassBoostEnabled = module == "bass",
            LowShelfGainDecibels = 18f,
            EqualizerEnabled = module == "equalizer",
            EqualizerLowGainDecibels = 12f,
            EqualizerMidGainDecibels = 12f,
            EqualizerHighGainDecibels = 12f,
        };
        var contaminated = new DspPipeline(48_000, settings);
        var reference = new DspPipeline(48_000, settings);
        float[] contaminatedBuffer = CreateContaminationProbe();
        float[] referenceBuffer = (float[])contaminatedBuffer.Clone();
        for (int index = 0; index < referenceBuffer.Length; index += 2)
        {
            referenceBuffer[index] = 0f;
        }

        DspMetrics metrics = contaminated.Process(contaminatedBuffer);
        reference.Process(referenceBuffer);

        Assert.True(metrics.SanitizedSamples > 0);
        for (int index = 1; index < contaminatedBuffer.Length; index += 2)
        {
            Assert.Equal(referenceBuffer[index], contaminatedBuffer[index]);
        }
    }

    [Fact]
    public void PipelineSanitizesNonFiniteWithoutClampingFiniteSamples()
    {
        var pipeline = new DspPipeline(48_000, TransparentSettings());
        float[] buffer =
        [
            float.NaN, float.PositiveInfinity,
            float.NegativeInfinity, 1e30f,
            -1e30f, 0.25f,
        ];

        DspMetrics metrics = pipeline.Process(buffer);

        Assert.All(buffer, sample => Assert.True(float.IsFinite(sample)));
        Assert.Equal(3, metrics.SanitizedSamples);
        Assert.Equal(0f, buffer[0]);
        Assert.Equal(float.MaxValue, buffer[1]);
        Assert.Equal(-float.MaxValue, buffer[2]);
        Assert.Equal(1e30f, buffer[3]);
    }

    [Theory]
    [InlineData(0f, 1d)]
    [InlineData(25f, 17.7827941003892d)]
    [InlineData(40f, 100d)]
    [InlineData(60f, 1000d)]
    [InlineData(75f, 5623.41325190349d)]
    public void RawGainUsesExactFormulaWithoutHiddenClamp(float decibels, double expected)
    {
        var pipeline = new DspPipeline(
            48_000,
            TransparentSettings(decibels) with { GainSmoothingMilliseconds = 0f });
        float[] buffer = [0.0001f, -0.0001f];

        DspMetrics metrics = pipeline.Process(buffer);

        Assert.Equal(decibels, metrics.AppliedMainGainDecibels);
        Assert.InRange(
            buffer[0],
            (float)(0.0001d * expected * (1d - 2e-6)),
            (float)(0.0001d * expected * (1d + 2e-6)));
        Assert.Equal(-buffer[0], buffer[1]);
    }

    [Fact]
    public void SeventyFiveDecibelGainDoesNotClampOneToSixtyFour()
    {
        var pipeline = new DspPipeline(
            48_000,
            TransparentSettings(75f) with { GainSmoothingMilliseconds = 0f });
        float[] buffer = [1f, -1f];

        DspMetrics metrics = pipeline.Process(buffer);

        Assert.InRange(buffer[0], 5623.40f, 5623.43f);
        Assert.Equal(-buffer[0], buffer[1]);
        Assert.True(metrics.AfterMainGainPeak > 64f);
        Assert.Equal(0, metrics.SanitizedSamples);
    }

    [Fact]
    public void SeventyFiveDecibelProcessDoesNotAllocate()
    {
        var pipeline = new DspPipeline(
            48_000,
            TransparentSettings(75f) with { GainSmoothingMilliseconds = 0f });
        var buffer = new float[2_048];
        Array.Fill(buffer, 0.001f);
        pipeline.Process(buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 100; iteration++)
        {
            pipeline.Process(buffer);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ProcessingAReusedBufferDoesNotAllocate()
    {
        var pipeline = new DspPipeline(48_000, DspSettings.Punch);
        float[] buffer = new float[1_024];
        pipeline.Process(buffer);
        pipeline.Process(buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            pipeline.Process(buffer);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void PipelineRejectsNonStereoBuffer()
    {
        var pipeline = new DspPipeline(48_000);
        Assert.Throws<ArgumentException>(() => pipeline.Process(new float[3]));
    }

    private static DspSettings TransparentSettings(float mainGainDecibels = 0f) =>
        new()
        {
            MainGainDecibels = mainGainDecibels,
            GainSmoothingMilliseconds = 0f,
            PanicSmoothingMilliseconds = 1.5f,
            DcBlockEnabled = false,
            BassBoostEnabled = false,
            EqualizerEnabled = false,
            CompressorEnabled = false,
            SoftClipperEnabled = false,
            LimiterEnabled = false,
            OutputGainDecibels = 0f,
        };

    private static float[] CreateStereoSine(
        int frameCount,
        float frequencyHz,
        float amplitude,
        int sampleRate = 0)
    {
        sampleRate = sampleRate == 0 ? frameCount : sampleRate;
        var buffer = new float[frameCount * 2];
        for (int frame = 0; frame < frameCount; frame++)
        {
            float sample = amplitude
                * MathF.Sin(2f * MathF.PI * frequencyHz * frame / sampleRate);
            buffer[frame * 2] = sample;
            buffer[(frame * 2) + 1] = sample;
        }

        return buffer;
    }

    private static double MeasureRms(float[] buffer)
    {
        double sum = 0;
        foreach (float sample in buffer)
        {
            sum += sample * sample;
        }

        return Math.Sqrt(sum / buffer.Length);
    }

    private static float[] CreateContaminationProbe()
    {
        var buffer = new float[64];
        for (int frame = 0; frame < buffer.Length / 2; frame++)
        {
            buffer[frame * 2] = frame switch
            {
                8 => float.MaxValue,
                9 => -float.MaxValue,
                _ => 0.1f,
            };
            buffer[(frame * 2) + 1] = 0.2f
                * MathF.Sin(2f * MathF.PI * frame / 17f);
        }

        return buffer;
    }
}
