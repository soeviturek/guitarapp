using GuitarPractice.App.Audio;
using GuitarPractice.App.Models;
using NAudio.Wave;

namespace GuitarPractice.App.Tests;

public sealed class AudioPipelineTests
{
    [Fact]
    public void TrackPlaybackResamplesMapsToStereoAndHonorsOffset()
    {
        var filePath = CreateSineWave();

        try
        {
            var model = new AudioTrackModel(
                filePath,
                "Test tone",
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(50),
                TrackKind.Imported);
            using var playback = new TrackPlayback(model, PracticeAudioEngine.OutputSampleRate);
            var framesPerBlock = PracticeAudioEngine.OutputSampleRate / 20;
            var samplesPerBlock = framesPerBlock * 2;
            var beforeOffset = new float[samplesPerBlock];
            var afterOffset = new float[samplesPerBlock];

            playback.Seek(0);
            playback.Read(beforeOffset, 0, beforeOffset.Length, 0);
            playback.Read(afterOffset, 0, afterOffset.Length, framesPerBlock);

            Assert.All(beforeOffset, sample => Assert.Equal(0, sample));
            Assert.Contains(afterOffset, sample => Math.Abs(sample) > 0.25f);

            for (var sample = 0; sample < afterOffset.Length; sample += 2)
            {
                Assert.Equal(afterOffset[sample], afterOffset[sample + 1], 5);
            }

            var seekBuffer = new float[960];
            var seekFrame = (long)(PracticeAudioEngine.OutputSampleRate * 0.1);
            playback.Seek(seekFrame);
            playback.Read(seekBuffer, 0, seekBuffer.Length, seekFrame);
            Assert.Contains(seekBuffer, sample => Math.Abs(sample) > 0.25f);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task WaveformExtractorReturnsVisiblePeaks()
    {
        var filePath = CreateSineWave();

        try
        {
            var peaks = await WaveformExtractor.ExtractAsync(filePath, 128);

            Assert.InRange(peaks.Length, 100, 128);
            Assert.True(peaks.Max() > 0.7f);
            Assert.All(peaks, peak => Assert.InRange(peak, 0, 1));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static string CreateSineWave()
    {
        var filePath = Path.Combine(
            Path.GetTempPath(),
            $"GuitarPractice-{Guid.NewGuid():N}.wav");
        var source = new FiniteSineProvider(44100, 440, TimeSpan.FromMilliseconds(200));
        WaveFileWriter.CreateWaveFile16(filePath, source);
        return filePath;
    }

    private sealed class FiniteSineProvider : ISampleProvider
    {
        private readonly double _frequency;
        private readonly long _totalSamples;
        private long _position;

        public FiniteSineProvider(int sampleRate, double frequency, TimeSpan duration)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            _frequency = frequency;
            _totalSamples = (long)Math.Round(duration.TotalSeconds * sampleRate);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var samplesToRead = (int)Math.Min(count, _totalSamples - _position);

            for (var sample = 0; sample < samplesToRead; sample++)
            {
                buffer[offset + sample] = (float)(
                    Math.Sin(2 * Math.PI * _frequency * _position / WaveFormat.SampleRate) * 0.8);
                _position++;
            }

            return samplesToRead;
        }
    }
}
