using GuitarPractice.Core;

namespace GuitarPractice.App.Audio;

internal sealed class ExercisePlayback
{
    private readonly int _sampleRate;
    private readonly Dictionary<int, float[]> _voices;

    public ExercisePlayback(PracticeExercise exercise, int sampleRate)
    {
        if (sampleRate < 8000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        Exercise = exercise;
        _sampleRate = sampleRate;
        _voices = exercise.Measures.SelectMany(measure => measure.Notes)
            .OfType<FretNote>()
            .Select(note => note.MidiNote)
            .Distinct()
            .ToDictionary(midiNote => midiNote, CreateVoice);
    }

    public PracticeExercise Exercise { get; }

    public void Mix(
        float[] output,
        int outputOffset,
        int frameCount,
        long timelineFrame,
        double beatsPerMinute,
        float volume)
    {
        var written = 0;
        var releaseFrames = Math.Max(1, _sampleRate / 200);
        while (written < frameCount)
        {
            var currentFrame = timelineFrame + written;
            var step = Exercise.GetStepAtFrame(currentFrame, _sampleRate, beatsPerMinute);
            if (step >= Exercise.StepCount)
            {
                break;
            }

            var startFrame = Exercise.GetStepFrame(step, _sampleRate, beatsPerMinute);
            var endFrame = Exercise.GetStepFrame(step + 1, _sampleRate, beatsPerMinute);
            var frames = (int)Math.Min(frameCount - written, endFrame - currentFrame);
            var note = Exercise.GetNote(step);
            if (note is not null)
            {
                var voice = _voices[note.MidiNote];
                for (var frame = 0; frame < frames; frame++)
                {
                    var localFrame = (int)(currentFrame - startFrame + frame);
                    var release = Math.Min(1, (endFrame - currentFrame - frame) / (double)releaseFrames);
                    var sample = voice[localFrame] * (float)release * volume;
                    var destination = outputOffset + (written + frame) * 2;
                    output[destination] += sample;
                    output[destination + 1] += sample;
                }
            }

            written += frames;
        }
    }

    private float[] CreateVoice(int midiNote)
    {
        var voice = new float[_sampleRate * 2];
        var frequency = 440 * Math.Pow(2, (midiNote - 69) / 12.0);
        for (var harmonic = 1; harmonic <= 6 && frequency * harmonic < _sampleRate / 2.0; harmonic++)
        {
            var angle = 2 * Math.PI * frequency * harmonic / _sampleRate;
            var rotationSin = Math.Sin(angle);
            var rotationCos = Math.Cos(angle);
            var sine = 0.0;
            var cosine = 1.0;
            var gain = 0.45 / Math.Pow(harmonic, 1.5);
            var decay = Math.Exp(-(2.5 + harmonic * 1.5) / _sampleRate);
            for (var frame = 0; frame < voice.Length; frame++)
            {
                voice[frame] += (float)(sine * gain);
                var nextSine = sine * rotationCos + cosine * rotationSin;
                cosine = cosine * rotationCos - sine * rotationSin;
                sine = nextSine;
                gain *= decay;
            }
        }

        var attackFrames = Math.Max(1, _sampleRate / 200);
        for (var frame = 0; frame < attackFrames; frame++)
        {
            voice[frame] *= (float)frame / attackFrames;
        }

        return voice;
    }
}
