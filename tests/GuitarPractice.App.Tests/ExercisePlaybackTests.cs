using GuitarPractice.App.Audio;
using GuitarPractice.App.Models;
using GuitarPractice.Core;
using NAudio.Wave;

namespace GuitarPractice.App.Tests;

public sealed class ExercisePlaybackTests
{
    private const int SampleRate = PracticeAudioEngine.OutputSampleRate;

    internal static PracticeExercise CreateExercise()
    {
        return new PracticeExercise("test", "Test", "Test exercise",
        [
            new ExerciseMeasure("A", [new(1, 5), null, new(2, 5), new(3, 5), null, new(4, 5), null, new(5, 5)])
        ]);
    }

    [Fact]
    public void TempoChangesKeepTheReferenceNoteWaveformAndPitch()
    {
        var playback = new ExercisePlayback(CreateExercise(), SampleRate);
        var slow = new float[SampleRate / 10];
        var fast = new float[slow.Length];

        playback.Mix(slow, 0, slow.Length / 2, 0, 60, 1);
        playback.Mix(fast, 0, fast.Length / 2, 0, 180, 1);

        Assert.Equal(slow, fast);
        Assert.Contains(slow, sample => Math.Abs(sample) > 0.1);
        for (var index = 0; index < slow.Length; index += 2)
        {
            Assert.Equal(slow[index], slow[index + 1]);
        }
    }

    [Fact]
    public void FifthFretOnHighEProducesConcertA()
    {
        var playback = new ExercisePlayback(CreateExercise(), SampleRate);
        var buffer = new float[SampleRate / 5 * 2];
        playback.Mix(buffer, 0, buffer.Length / 2, 0, 60, 1);

        var expectedEnergy = GetFrequencyEnergy(buffer, 440);
        Assert.True(expectedEnergy > GetFrequencyEnergy(buffer, 440 * Math.Pow(2, 1.0 / 12)) * 10);
        Assert.True(expectedEnergy > GetFrequencyEnergy(buffer, 440 / Math.Pow(2, 1.0 / 12)) * 10);
    }

    [Fact]
    public void RestSlotsDoNotModifyTheMix()
    {
        var exercise = CreateExercise();
        var playback = new ExercisePlayback(exercise, SampleRate);
        var buffer = Enumerable.Repeat(0.25f, 1000).ToArray();

        playback.Mix(buffer, 10, 400, exercise.GetStepFrame(1, SampleRate, 137), 137, 1);

        Assert.All(buffer, sample => Assert.Equal(0.25f, sample));
    }

    [Fact]
    public void SeekingMatchesContinuousAudioAcrossNotesAndRests()
    {
        var exercise = CreateExercise();
        var playback = new ExercisePlayback(exercise, SampleRate);
        const int start = 20123;
        var continuous = new float[SampleRate * 2];
        var seeked = new float[10000];
        playback.Mix(continuous, 0, continuous.Length / 2, 0, 137, 1);
        playback.Mix(seeked, 0, seeked.Length / 2, start, 137, 1);

        Assert.Equal(continuous.Skip(start * 2).Take(seeked.Length), seeked);
    }

    [Fact]
    public void MixRespectsTheDestinationOffsetAndReleaseEnvelope()
    {
        var exercise = CreateExercise();
        var playback = new ExercisePlayback(exercise, SampleRate);
        var buffer = new float[1200];
        var end = exercise.GetStepFrame(1, SampleRate, 120);
        playback.Mix(buffer, 20, 500, end - 500, 120, 1);

        Assert.All(buffer.Take(20), sample => Assert.Equal(0, sample));
        Assert.All(buffer.Skip(1020), sample => Assert.Equal(0, sample));
        Assert.InRange(Math.Abs(buffer[1018]), 0, 0.005f);
    }

    [Fact]
    public void EngineRetimesExercisePositionAndLoopWhilePlaying()
    {
        using var engine = new PracticeAudioEngine(new FakeWavePlayer());
        engine.SetExercise(CreateExercise());
        engine.SetLoopRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1.5));
        engine.SetLoopEnabled(true);
        engine.Seek(TimeSpan.FromSeconds(1));
        engine.Play();

        engine.BeatsPerMinute = 60;

        Assert.True(engine.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(4), engine.Duration);
        Assert.Equal(TimeSpan.FromSeconds(2), engine.Position);
        Assert.Equal(TimeSpan.FromSeconds(1), engine.LoopStart);
        Assert.Equal(TimeSpan.FromSeconds(3), engine.LoopEnd);
    }

    [Fact]
    public void ExerciseLoopWrapsInsideTheAudioBuffer()
    {
        using var engine = new PracticeAudioEngine(new FakeWavePlayer());
        engine.SetExercise(CreateExercise());
        engine.SetLoopRange(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1));
        engine.SetLoopEnabled(true);
        engine.Seek(TimeSpan.FromSeconds(0.99));
        engine.Play();
        var buffer = new float[9600];

        Assert.Equal(buffer.Length, engine.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.59, engine.Position.TotalSeconds, 5);
        Assert.Contains(buffer, sample => Math.Abs(sample) > 0.01);
    }

    [Fact]
    public void MetronomeTempoDoesNotRetimeTheAudioWorkspace()
    {
        using var engine = new PracticeAudioEngine(new FakeWavePlayer());
        engine.SetLoopRange(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12));
        engine.Seek(TimeSpan.FromSeconds(10));
        var duration = engine.Duration;

        engine.BeatsPerMinute = 60;

        Assert.Equal(duration, engine.Duration);
        Assert.Equal(TimeSpan.FromSeconds(10), engine.Position);
        Assert.Equal(TimeSpan.FromSeconds(3), engine.LoopStart);
        Assert.Equal(TimeSpan.FromSeconds(12), engine.LoopEnd);
    }

    [Fact]
    public void ExerciseModeSilencesButDoesNotRemoveOrRetimeImportedAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GuitarPractice-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)))
            {
                var samples = Enumerable.Repeat(0.25f, SampleRate * 4).ToArray();
                writer.WriteSamples(samples, 0, samples.Length);
            }
            using var engine = new PracticeAudioEngine(new FakeWavePlayer());
            engine.AddTrack(new AudioTrackModel(path, "Audio", TimeSpan.FromSeconds(2), TimeSpan.Zero, TrackKind.Imported));
            engine.SetExercise(CreateExercise());
            engine.ExerciseSoundEnabled = false;
            engine.BeatsPerMinute = 60;
            engine.Play();
            var buffer = new float[1000];
            engine.Read(buffer, 0, buffer.Length);
            Assert.All(buffer, sample => Assert.Equal(0, sample));

            engine.SetExercise(null);
            engine.Play();
            engine.Read(buffer, 0, buffer.Length);

            Assert.Equal(TimeSpan.FromSeconds(2), engine.Duration);
            Assert.All(buffer, sample => Assert.InRange(sample, 0.19f, 0.2f));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static double GetFrequencyEnergy(float[] stereo, double frequency)
    {
        var real = 0.0;
        var imaginary = 0.0;
        for (var frame = SampleRate / 50; frame < stereo.Length / 2; frame++)
        {
            var angle = 2 * Math.PI * frequency * frame / SampleRate;
            real += stereo[frame * 2] * Math.Cos(angle);
            imaginary += stereo[frame * 2] * Math.Sin(angle);
        }
        return real * real + imaginary * imaginary;
    }
}
