using GuitarPractice.App.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GuitarPractice.App.Audio;

internal sealed class TrackPlayback : IDisposable
{
    private readonly int _outputSampleRate;
    private AudioFileReader? _reader;
    private ISampleProvider? _sampleProvider;
    private long _nextTimelineFrame = -1;

    public TrackPlayback(AudioTrackModel model, int outputSampleRate)
    {
        Model = model;
        _outputSampleRate = outputSampleRate;
    }

    public AudioTrackModel Model { get; }

    public void Seek(long timelineFrame)
    {
        var offsetFrame = ToOutputFrames(Model.Offset);
        var localFrame = Math.Max(0, timelineFrame - offsetFrame);

        _reader ??= new AudioFileReader(Model.FilePath);
        _reader.CurrentTime = TimeSpan.FromSeconds((double)localFrame / _outputSampleRate);

        ISampleProvider provider = new StereoSampleProvider(_reader);
        if (provider.WaveFormat.SampleRate != _outputSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, _outputSampleRate);
        }

        _sampleProvider = provider;
        _nextTimelineFrame = timelineFrame;
    }

    public void Read(float[] buffer, int offset, int count, long timelineStartFrame)
    {
        var requestedFrames = count / 2;
        if (requestedFrames == 0)
        {
            return;
        }

        if (_sampleProvider is null || _nextTimelineFrame != timelineStartFrame)
        {
            Seek(timelineStartFrame);
        }

        var offsetFrame = ToOutputFrames(Model.Offset);
        var localStartFrame = timelineStartFrame - offsetFrame;
        var trackDurationFrames = ToOutputFrames(Model.Duration);

        if (localStartFrame >= trackDurationFrames)
        {
            _nextTimelineFrame = timelineStartFrame + requestedFrames;
            return;
        }

        var leadingSilentFrames = localStartFrame < 0
            ? (int)Math.Min(requestedFrames, -localStartFrame)
            : 0;
        var readableFrames = requestedFrames - leadingSilentFrames;

        if (readableFrames > 0)
        {
            _sampleProvider!.Read(
                buffer,
                offset + leadingSilentFrames * 2,
                readableFrames * 2);
        }

        _nextTimelineFrame = timelineStartFrame + requestedFrames;
    }

    public void Dispose()
    {
        DisposeSource();
    }

    private long ToOutputFrames(TimeSpan time)
    {
        return (long)Math.Round(time.TotalSeconds * _outputSampleRate);
    }

    private void DisposeSource()
    {
        _reader?.Dispose();
        _reader = null;
        _sampleProvider = null;
    }
}
