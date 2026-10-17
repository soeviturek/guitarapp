using GuitarPractice.App.Models;

namespace GuitarPractice.App.Persistence;

public sealed class PracticeSession
{
    public List<TrackSessionEntry> Tracks { get; init; } = [];

    public double BeatsPerMinute { get; init; } = 120;

    public bool MetronomeEnabled { get; init; }

    public double MetronomeVolume { get; init; } = 0.65;

    public double MasterVolume { get; init; } = 0.9;

    public bool LoopEnabled { get; init; }

    public double LoopStartSeconds { get; init; }

    public double LoopEndSeconds { get; init; }

    public string? RecordingInputId { get; init; }
}

public sealed class TrackSessionEntry
{
    public required string FilePath { get; init; }

    public required string Name { get; init; }

    public TrackKind Kind { get; init; }

    public double OffsetSeconds { get; init; }

    public double Volume { get; init; } = 0.85;

    public bool IsMuted { get; init; }
}
