using GuitarPractice.Core;

namespace GuitarPractice.Core.Tests;

public sealed class MetronomeSignalTests
{
    [Fact]
    public void ProducesClickAtBeatAndSilenceBetweenBeats()
    {
        const int sampleRate = 48000;

        var click = MetronomeSignal.GetSample(0, sampleRate, 120);
        var betweenBeats = MetronomeSignal.GetSample(sampleRate / 4, sampleRate, 120);

        Assert.NotEqual(0, click);
        Assert.Equal(0, betweenBeats);
    }

    [Fact]
    public void ClickStopsBeforeFortyMilliseconds()
    {
        const int sampleRate = 48000;

        var afterClick = MetronomeSignal.GetSample(
            (long)(sampleRate * 0.04),
            sampleRate,
            120);

        Assert.Equal(0, afterClick);
    }

    [Fact]
    public void AccentHasMoreEnergyThanRegularBeat()
    {
        const int sampleRate = 48000;
        const double beatsPerMinute = 120;
        var framesPerBeat = sampleRate / 2;
        var comparisonFrames = (int)(sampleRate * 0.04);

        var accentEnergy = GetClickEnergy(0, comparisonFrames, sampleRate, beatsPerMinute);
        var regularEnergy = GetClickEnergy(
            framesPerBeat,
            comparisonFrames,
            sampleRate,
            beatsPerMinute);

        Assert.True(accentEnergy > regularEnergy * 1.1);
    }

    [Fact]
    public void GeneratedClickIsDeterministic()
    {
        const int sampleRate = 48000;
        const long frame = 123;

        var first = MetronomeSignal.GetSample(frame, sampleRate, 137);
        var second = MetronomeSignal.GetSample(frame, sampleRate, 137);

        Assert.Equal(first, second);
    }

    private static double GetClickEnergy(
        long startFrame,
        int frameCount,
        int sampleRate,
        double beatsPerMinute)
    {
        var energy = 0.0;

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = MetronomeSignal.GetSample(
                startFrame + frame,
                sampleRate,
                beatsPerMinute);
            energy += sample * sample;
        }

        return energy;
    }
}
