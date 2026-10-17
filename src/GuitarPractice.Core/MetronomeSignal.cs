namespace GuitarPractice.Core;

public static class MetronomeSignal
{
    private const double RegularClickDurationSeconds = 0.028;
    private const double AccentClickDurationSeconds = 0.034;
    private const double TailFadeSeconds = 0.002;

    public static float GetSample(
        long timelineFrame,
        int sampleRate,
        double beatsPerMinute,
        int beatsPerBar = 4)
    {
        if (timelineFrame < 0 || sampleRate <= 0 || beatsPerMinute is < 20 or > 400 || beatsPerBar <= 0)
        {
            return 0;
        }

        var framesPerBeat = sampleRate * 60.0 / beatsPerMinute;
        var beatIndex = (long)Math.Floor(timelineFrame / framesPerBeat);
        var beatStartFrame = (long)Math.Round(beatIndex * framesPerBeat);
        var frameWithinClick = timelineFrame - beatStartFrame;
        var isAccent = beatIndex % beatsPerBar == 0;
        var clickDuration = isAccent
            ? AccentClickDurationSeconds
            : RegularClickDurationSeconds;
        var clickLength = (long)Math.Ceiling(sampleRate * clickDuration);

        if (frameWithinClick < 0 || frameWithinClick >= clickLength)
        {
            return 0;
        }

        var elapsedSeconds = (double)frameWithinClick / sampleRate;
        var bodyFrequency = isAccent ? 1240.0 : 930.0;
        var overtoneFrequency = isAccent ? 2110.0 : 1570.0;
        var bodyDecay = isAccent ? 0.0075 : 0.0065;
        var amplitude = isAccent ? 0.88 : 0.72;

        var body = Math.Cos(2 * Math.PI * bodyFrequency * elapsedSeconds) *
                   Math.Exp(-elapsedSeconds / bodyDecay);
        var overtone = Math.Cos(2 * Math.PI * overtoneFrequency * elapsedSeconds + 0.22) *
                       Math.Exp(-elapsedSeconds / 0.0032);
        var strike = GetDeterministicNoise(beatIndex, frameWithinClick) *
                     Math.Exp(-elapsedSeconds / 0.0014);

        var tailGain = Math.Clamp(
            (clickDuration - elapsedSeconds) / TailFadeSeconds,
            0,
            1);
        var sample = (body * 0.68 + overtone * 0.23 + strike * 0.09) *
                     amplitude *
                     tailGain;

        return (float)Math.Clamp(sample, -1, 1);
    }

    private static double GetDeterministicNoise(long beatIndex, long frameWithinClick)
    {
        unchecked
        {
            var value = (ulong)beatIndex * 0x9E3779B97F4A7C15UL;
            value ^= (ulong)frameWithinClick * 0xBF58476D1CE4E5B9UL;
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;

            return ((value >> 40) / 8388607.5) - 1;
        }
    }
}
