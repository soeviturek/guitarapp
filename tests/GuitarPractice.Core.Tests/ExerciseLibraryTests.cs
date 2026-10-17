using GuitarPractice.Core;

namespace GuitarPractice.Core.Tests;

public sealed class ExerciseLibraryTests
{
    [Fact]
    public void PresetsHaveUniqueIdsPlayableNotesAndCompleteMeasures()
    {
        Assert.Equal(4, ExerciseLibrary.All.Count);
        Assert.Equal(4, ExerciseLibrary.All.Select(exercise => exercise.Id).Distinct().Count());
        foreach (var exercise in ExerciseLibrary.All)
        {
            Assert.NotEmpty(exercise.Measures);
            Assert.All(exercise.Measures, measure =>
            {
                Assert.Equal(8, measure.Notes.Count);
                Assert.All(measure.Notes.OfType<FretNote>(), note =>
                {
                    Assert.InRange(note.StringNumber, 1, 6);
                    Assert.InRange(note.Fret, 0, 24);
                    Assert.InRange(note.MidiNote, 40, 88);
                });
            });
        }
    }

    [Theory]
    [InlineData(1, 0, 64, "E4")]
    [InlineData(5, 3, 48, "C3")]
    [InlineData(6, 0, 40, "E2")]
    [InlineData(1, 24, 88, "E6")]
    public void NotesUseStandardGuitarTuning(int stringNumber, int fret, int midiNote, string name)
    {
        var note = new FretNote(stringNumber, fret);

        Assert.Equal(midiNote, note.MidiNote);
        Assert.Equal(name, note.PitchName);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 25)]
    public void InvalidFingeringsAreRejected(int stringNumber, int fret)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FretNote(stringNumber, fret));
    }

    [Fact]
    public void DiatonicArpeggiosUseCorrectChordTonesAndReturnToTheirRoots()
    {
        var chords = new Dictionary<string, int[]>
        {
            ["C"] = [0, 4, 7],
            ["Dm"] = [2, 5, 9],
            ["Em"] = [4, 7, 11],
            ["F"] = [5, 9, 0],
            ["G"] = [7, 11, 2],
            ["Am"] = [9, 0, 4],
            ["Bdim"] = [11, 2, 5]
        };
        var exercise = ExerciseLibrary.All.Single(candidate => candidate.Id == "c-diatonic-arpeggios");
        var phrases = exercise.Measures.GroupBy(measure => measure.Label).ToList();

        Assert.Equal(35, phrases.Count);
        foreach (var phrase in phrases)
        {
            var chordName = phrase.Key.Split(' ')[0];
            var expected = chords[chordName];
            var notes = phrase.SelectMany(measure => measure.Notes).OfType<FretNote>().ToList();
            Assert.Equal(expected[0], notes[0].MidiNote % 12);
            Assert.Equal(notes[0].MidiNote, notes[^1].MidiNote);
            Assert.All(notes, note => Assert.Contains(note.MidiNote % 12, expected));
        }
        Assert.Contains(exercise.Measures.SelectMany(measure => measure.Notes).OfType<FretNote>(),
            note => note.Fret == 24);
    }

    [Theory]
    [InlineData("c-major", new[] { 0, 2, 4, 5, 7, 9, 11 })]
    [InlineData("a-minor-pentatonic", new[] { 9, 0, 2, 4, 7 })]
    public void ScalesOnlyUseTheirPitchClasses(string id, int[] pitchClasses)
    {
        var exercise = ExerciseLibrary.All.Single(candidate => candidate.Id == id);
        var notes = exercise.Measures.SelectMany(measure => measure.Notes).OfType<FretNote>().ToList();

        Assert.Equal(pitchClasses[0], notes[0].MidiNote % 12);
        Assert.All(notes, note => Assert.Contains(note.MidiNote % 12, pitchClasses));
        Assert.Contains(notes, note => note.Fret >= 20);
    }

    [Fact]
    public void ChromaticPresetCoversEveryFrettedPositionOnEveryString()
    {
        var exercise = ExerciseLibrary.All.Single(candidate => candidate.Id == "chromatic-four-fingers");
        var fingerings = exercise.Measures.SelectMany(measure => measure.Notes).OfType<FretNote>().ToHashSet();

        for (var stringNumber = 1; stringNumber <= 6; stringNumber++)
        {
            for (var fret = 1; fret <= 24; fret++)
            {
                Assert.Contains(new FretNote(stringNumber, fret), fingerings);
            }
        }
    }

    [Theory]
    [InlineData(40)]
    [InlineData(83)]
    [InlineData(137)]
    [InlineData(240)]
    [InlineData(400)]
    public void AudioAndScoreAgreeAtEveryRoundedNoteBoundary(double bpm)
    {
        const int sampleRate = 48000;
        var exercise = ExerciseLibrary.All[0];
        for (var step = 0; step <= exercise.StepCount; step++)
        {
            var frame = exercise.GetStepFrame(step, sampleRate, bpm);
            Assert.Equal(step, exercise.GetStepAtFrame(frame, sampleRate, bpm));
            if (step > 0)
            {
                Assert.Equal(step - 1, exercise.GetStepAtFrame(frame - 1, sampleRate, bpm));
            }
        }
    }

    [Fact]
    public void HalvingTempoDoublesDurationWithoutChangingNotes()
    {
        var exercise = ExerciseLibrary.All[0];

        Assert.Equal(exercise.GetDuration(120).TotalSeconds * 2, exercise.GetDuration(60).TotalSeconds);
        Assert.Equal(exercise.GetStepTime(17, 120).TotalSeconds * 2,
            exercise.GetStepTime(17, 60).TotalSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => exercise.GetDuration(double.NaN));
    }
}
