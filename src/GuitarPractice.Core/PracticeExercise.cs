namespace GuitarPractice.Core;

public sealed record FretNote
{
    private static readonly int[] OpenStringMidiNotes = [64, 59, 55, 50, 45, 40];
    private static readonly string[] PitchNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public FretNote(int stringNumber, int fret)
    {
        if (stringNumber is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(stringNumber));
        }
        if (fret is < 0 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(fret));
        }

        StringNumber = stringNumber;
        Fret = fret;
    }

    public int StringNumber { get; }

    public int Fret { get; }

    public int MidiNote => OpenStringMidiNotes[StringNumber - 1] + Fret;

    public string PitchName => $"{PitchNames[MidiNote % 12]}{MidiNote / 12 - 1}";
}

public sealed class ExerciseMeasure
{
    public const int StepsPerMeasure = 8;

    public ExerciseMeasure(string label, IEnumerable<FretNote?> notes)
    {
        var steps = notes.ToArray();
        if (steps.Length != StepsPerMeasure)
        {
            throw new ArgumentException("A measure must contain eight eighth-note slots.", nameof(notes));
        }

        Label = label;
        Notes = Array.AsReadOnly(steps);
    }

    public string Label { get; }

    public IReadOnlyList<FretNote?> Notes { get; }
}

public sealed class PracticeExercise
{
    public const double ReferenceBeatsPerMinute = 120;
    public const double BeatsPerStep = 0.5;

    public PracticeExercise(
        string id,
        string name,
        string description,
        IEnumerable<ExerciseMeasure> measures)
    {
        var bars = measures.ToArray();
        if (bars.Length == 0)
        {
            throw new ArgumentException("An exercise must contain at least one measure.", nameof(measures));
        }

        Id = id;
        Name = name;
        Description = description;
        Measures = Array.AsReadOnly(bars);
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public IReadOnlyList<ExerciseMeasure> Measures { get; }

    public int StepCount => Measures.Count * ExerciseMeasure.StepsPerMeasure;

    public FretNote? GetNote(int step)
    {
        if (step < 0 || step >= StepCount)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        return Measures[step / ExerciseMeasure.StepsPerMeasure]
            .Notes[step % ExerciseMeasure.StepsPerMeasure];
    }

    public TimeSpan GetStepTime(int step, double beatsPerMinute)
    {
        if (step < 0 || step > StepCount)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }
        ValidateTempo(beatsPerMinute);
        return TimeSpan.FromSeconds(step * BeatsPerStep * 60 / beatsPerMinute);
    }

    public TimeSpan GetDuration(double beatsPerMinute) => GetStepTime(StepCount, beatsPerMinute);

    public long GetStepFrame(int step, int sampleRate, double beatsPerMinute)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        return (long)Math.Round(GetStepTime(step, beatsPerMinute).TotalSeconds * sampleRate);
    }

    public int GetStepAtFrame(long frame, int sampleRate, double beatsPerMinute)
    {
        if (frame < 0 || sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(frame < 0 ? nameof(frame) : nameof(sampleRate));
        }
        ValidateTempo(beatsPerMinute);

        var framesPerStep = sampleRate * BeatsPerStep * 60 / beatsPerMinute;
        var step = (int)Math.Min(StepCount, Math.Floor(frame / framesPerStep));
        if (step < StepCount && frame >= GetStepFrame(step + 1, sampleRate, beatsPerMinute))
        {
            step++;
        }

        return step;
    }

    private static void ValidateTempo(double beatsPerMinute)
    {
        if (!double.IsFinite(beatsPerMinute) || beatsPerMinute is < 20 or > 400)
        {
            throw new ArgumentOutOfRangeException(nameof(beatsPerMinute));
        }
    }
}
