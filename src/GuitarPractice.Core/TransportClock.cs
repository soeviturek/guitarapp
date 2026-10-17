namespace GuitarPractice.Core;

public sealed class TransportClock
{
    private const double MinimumLoopSeconds = 0.05;

    public TransportClock(int sampleRate, TimeSpan initialDuration)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        SampleRate = sampleRate;
        SetDuration(initialDuration);
    }

    public int SampleRate { get; }

    public long PositionFrame { get; private set; }

    public long DurationFrames { get; private set; }

    public long LoopStartFrame { get; private set; }

    public long LoopEndFrame { get; private set; }

    public bool IsLoopEnabled { get; private set; }

    public TimeSpan Position => FramesToTime(PositionFrame);

    public TimeSpan Duration => FramesToTime(DurationFrames);

    public TimeSpan LoopStart => FramesToTime(LoopStartFrame);

    public TimeSpan LoopEnd => FramesToTime(LoopEndFrame);

    public bool HasValidLoop => LoopEndFrame - LoopStartFrame >= SecondsToFrames(MinimumLoopSeconds);

    public void SetDuration(TimeSpan duration)
    {
        var previousDurationFrames = DurationFrames;
        var loopCoveredEntireDuration =
            LoopStartFrame == 0 &&
            (LoopEndFrame == 0 || LoopEndFrame == previousDurationFrames);

        DurationFrames = Math.Max(1, TimeToFrames(duration));
        PositionFrame = Math.Clamp(PositionFrame, 0, DurationFrames);
        LoopStartFrame = Math.Clamp(LoopStartFrame, 0, DurationFrames);
        LoopEndFrame = loopCoveredEntireDuration
            ? DurationFrames
            : Math.Clamp(LoopEndFrame, 0, DurationFrames);

        if (!HasValidLoop)
        {
            LoopStartFrame = 0;
            LoopEndFrame = DurationFrames;
        }
    }

    public void Seek(TimeSpan position)
    {
        PositionFrame = Math.Clamp(TimeToFrames(position), 0, DurationFrames);
    }

    public void Retime(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var previousDuration = DurationFrames;
        var position = PositionFrame;
        var loopStart = LoopStartFrame;
        var loopEnd = LoopEndFrame;
        SetDuration(duration);
        var factor = (double)DurationFrames / previousDuration;
        PositionFrame = Math.Clamp((long)Math.Round(position * factor), 0, DurationFrames);
        LoopStartFrame = Math.Clamp((long)Math.Round(loopStart * factor), 0, DurationFrames);
        LoopEndFrame = Math.Clamp((long)Math.Round(loopEnd * factor), 0, DurationFrames);

        if (!HasValidLoop)
        {
            var minimumLength = Math.Min(DurationFrames, SecondsToFrames(MinimumLoopSeconds));
            LoopStartFrame = Math.Min(LoopStartFrame, DurationFrames - minimumLength);
            LoopEndFrame = Math.Max(LoopEndFrame, LoopStartFrame + minimumLength);
        }
        IsLoopEnabled &= HasValidLoop;
    }

    public void SetLoopStart(TimeSpan position)
    {
        var requestedFrame = Math.Clamp(TimeToFrames(position), 0, DurationFrames);
        var minimumLength = SecondsToFrames(MinimumLoopSeconds);
        LoopStartFrame = Math.Min(requestedFrame, Math.Max(0, LoopEndFrame - minimumLength));
    }

    public void SetLoopEnd(TimeSpan position)
    {
        var requestedFrame = Math.Clamp(TimeToFrames(position), 0, DurationFrames);
        var minimumLength = SecondsToFrames(MinimumLoopSeconds);
        LoopEndFrame = Math.Max(requestedFrame, Math.Min(DurationFrames, LoopStartFrame + minimumLength));
    }

    public void SetLoopRange(TimeSpan start, TimeSpan end)
    {
        var startFrame = Math.Clamp(TimeToFrames(start), 0, DurationFrames);
        var endFrame = Math.Clamp(TimeToFrames(end), 0, DurationFrames);
        var minimumLength = SecondsToFrames(MinimumLoopSeconds);

        if (endFrame - startFrame < minimumLength)
        {
            throw new ArgumentException("Loop range must be at least 50 milliseconds long.");
        }

        LoopStartFrame = startFrame;
        LoopEndFrame = endFrame;
    }

    public void SetLoopEnabled(bool enabled)
    {
        IsLoopEnabled = enabled && HasValidLoop;
    }

    public int FramesUntilBoundary(int requestedFrames)
    {
        if (requestedFrames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        }

        var boundary = IsLoopEnabled ? LoopEndFrame : DurationFrames;
        return (int)Math.Min(requestedFrames, Math.Max(0, boundary - PositionFrame));
    }

    public TransportAdvanceResult Advance(int frames)
    {
        if (frames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frames));
        }

        PositionFrame = Math.Min(PositionFrame + frames, DurationFrames);

        if (IsLoopEnabled && PositionFrame >= LoopEndFrame)
        {
            PositionFrame = LoopStartFrame;
            return TransportAdvanceResult.Looped;
        }

        return PositionFrame >= DurationFrames
            ? TransportAdvanceResult.Ended
            : TransportAdvanceResult.Advanced;
    }

    private long TimeToFrames(TimeSpan time)
    {
        return SecondsToFrames(time.TotalSeconds);
    }

    private long SecondsToFrames(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            return 0;
        }

        return (long)Math.Round(Math.Max(0, seconds) * SampleRate);
    }

    private TimeSpan FramesToTime(long frames)
    {
        return TimeSpan.FromSeconds((double)frames / SampleRate);
    }
}
