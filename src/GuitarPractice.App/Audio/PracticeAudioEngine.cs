using GuitarPractice.App.Models;
using GuitarPractice.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GuitarPractice.App.Audio;

public sealed class PracticeAudioEngine : ISampleProvider, IDisposable
{
    public const int OutputSampleRate = 48000;
    private static readonly TimeSpan EmptySessionDuration = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();
    private readonly List<TrackPlayback> _tracks = [];
    private readonly TransportClock _clock = new(OutputSampleRate, EmptySessionDuration);
    private readonly IWavePlayer _output;
    private float[] _trackBuffer = [];
    private ExercisePlayback? _exercisePlayback;
    private bool _exerciseSoundEnabled = true;
    private float _exerciseVolume = 0.55f;
    private bool _isPlaying;
    private bool _endEventRaised;
    private bool _metronomeEnabled;
    private float _metronomeVolume = 0.65f;
    private float _masterVolume = 0.9f;
    private double _beatsPerMinute = 120;

    public PracticeAudioEngine(IWavePlayer? output = null)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(OutputSampleRate, 2);
        _output = output ?? new WasapiOut(AudioClientShareMode.Shared, true, 60);
        _output.Init(new SampleToWaveProvider(this));
        _output.PlaybackStopped += Output_PlaybackStopped;
    }

    public event EventHandler? PlaybackEnded;

    public event EventHandler? LoopCompleted;

    public event EventHandler<StoppedEventArgs>? PlaybackFaulted;

    public WaveFormat WaveFormat { get; }

    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _isPlaying;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return _clock.Position;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return _clock.Duration;
            }
        }
    }

    public TimeSpan LoopStart
    {
        get
        {
            lock (_gate)
            {
                return _clock.LoopStart;
            }
        }
    }

    public TimeSpan LoopEnd
    {
        get
        {
            lock (_gate)
            {
                return _clock.LoopEnd;
            }
        }
    }

    public bool IsLoopEnabled
    {
        get
        {
            lock (_gate)
            {
                return _clock.IsLoopEnabled;
            }
        }
    }

    public double BeatsPerMinute
    {
        get
        {
            lock (_gate)
            {
                return _beatsPerMinute;
            }
        }
        set
        {
            if (!double.IsFinite(value) || value is < 20 or > 400)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "BPM must be between 20 and 400.");
            }

            lock (_gate)
            {
                if (_exercisePlayback is not null)
                {
                    _clock.Retime(_exercisePlayback.Exercise.GetDuration(value));
                }
                _beatsPerMinute = value;
            }
        }
    }

    public bool ExerciseSoundEnabled
    {
        get
        {
            lock (_gate)
            {
                return _exerciseSoundEnabled;
            }
        }
        set
        {
            lock (_gate)
            {
                _exerciseSoundEnabled = value;
            }
        }
    }

    public float ExerciseVolume
    {
        get
        {
            lock (_gate)
            {
                return _exerciseVolume;
            }
        }
        set
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            lock (_gate)
            {
                _exerciseVolume = Math.Clamp(value, 0, 1);
            }
        }
    }

    public void SetExercise(PracticeExercise? exercise)
    {
        var playback = exercise is null ? null : new ExercisePlayback(exercise, OutputSampleRate);
        Pause();
        lock (_gate)
        {
            _exercisePlayback = playback;
            RecalculateDuration();
            _clock.SetLoopRange(TimeSpan.Zero, _clock.Duration);
            _clock.SetLoopEnabled(false);
            SeekInternal(TimeSpan.Zero);
            _endEventRaised = false;
        }
    }

    public bool MetronomeEnabled
    {
        get
        {
            lock (_gate)
            {
                return _metronomeEnabled;
            }
        }
        set
        {
            lock (_gate)
            {
                _metronomeEnabled = value;
            }
        }
    }

    public float MetronomeVolume
    {
        get
        {
            lock (_gate)
            {
                return _metronomeVolume;
            }
        }
        set
        {
            lock (_gate)
            {
                _metronomeVolume = Math.Clamp(value, 0, 1);
            }
        }
    }

    public float MasterVolume
    {
        get
        {
            lock (_gate)
            {
                return _masterVolume;
            }
        }
        set
        {
            lock (_gate)
            {
                _masterVolume = Math.Clamp(value, 0, 1);
            }
        }
    }

    public void AddTrack(AudioTrackModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        lock (_gate)
        {
            var playback = new TrackPlayback(model, OutputSampleRate);
            try
            {
                playback.Seek(_clock.PositionFrame);
                _tracks.Add(playback);
                RecalculateDuration();
            }
            catch
            {
                _tracks.Remove(playback);
                playback.Dispose();
                throw;
            }
        }
    }

    public void RemoveTrack(Guid id)
    {
        lock (_gate)
        {
            var track = _tracks.FirstOrDefault(candidate => candidate.Model.Id == id);
            if (track is null)
            {
                return;
            }

            _tracks.Remove(track);
            track.Dispose();
            RecalculateDuration();
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_clock.PositionFrame >= _clock.DurationFrames)
            {
                SeekInternal(TimeSpan.Zero);
            }

            _isPlaying = true;
            _endEventRaised = false;
        }

        _output.Play();
    }

    public void Pause()
    {
        _output.Pause();

        lock (_gate)
        {
            _isPlaying = false;
        }
    }

    public void Stop()
    {
        Pause();
        Seek(TimeSpan.Zero);
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            SeekInternal(position);
            _endEventRaised = false;
        }
    }

    public void SetLoopStart(TimeSpan position)
    {
        lock (_gate)
        {
            _clock.SetLoopStart(position);
        }
    }

    public void SetLoopEnd(TimeSpan position)
    {
        lock (_gate)
        {
            _clock.SetLoopEnd(position);
        }
    }

    public void SetLoopRange(TimeSpan start, TimeSpan end)
    {
        lock (_gate)
        {
            _clock.SetLoopRange(start, end);
        }
    }

    public void SetLoopEnabled(bool enabled)
    {
        lock (_gate)
        {
            _clock.SetLoopEnabled(enabled);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var loopsCompleted = 0;
        var playbackEnded = false;
        var samplesWritten = 0;

        lock (_gate)
        {
            Array.Clear(buffer, offset, count);

            if (!_isPlaying)
            {
                return 0;
            }

            var requestedFrames = count / WaveFormat.Channels;
            var framesWritten = 0;

            while (framesWritten < requestedFrames)
            {
                var segmentFrames = _clock.FramesUntilBoundary(requestedFrames - framesWritten);
                if (segmentFrames == 0)
                {
                    var boundaryResult = _clock.Advance(0);
                    if (boundaryResult == TransportAdvanceResult.Looped)
                    {
                        SeekTracksToClock();
                        loopsCompleted++;
                        continue;
                    }

                    playbackEnded = boundaryResult == TransportAdvanceResult.Ended;
                    break;
                }

                var segmentSamples = segmentFrames * WaveFormat.Channels;
                var destinationOffset = offset + framesWritten * WaveFormat.Channels;
                if (_exercisePlayback is not null)
                {
                    if (_exerciseSoundEnabled)
                    {
                        _exercisePlayback.Mix(
                            buffer, destinationOffset, segmentFrames, _clock.PositionFrame,
                            _beatsPerMinute, _exerciseVolume);
                    }
                }
                else
                {
                    MixTracks(buffer, destinationOffset, segmentSamples, _clock.PositionFrame);
                }
                ApplyLoopEdgeFade(buffer, destinationOffset, segmentFrames, _clock.PositionFrame);
                MixMetronome(buffer, destinationOffset, segmentFrames, _clock.PositionFrame);
                ApplyMasterVolume(buffer, destinationOffset, segmentSamples);

                framesWritten += segmentFrames;
                var advanceResult = _clock.Advance(segmentFrames);

                if (advanceResult == TransportAdvanceResult.Looped)
                {
                    SeekTracksToClock();
                    loopsCompleted++;
                }
                else if (advanceResult == TransportAdvanceResult.Ended)
                {
                    playbackEnded = true;
                    break;
                }
            }

            samplesWritten = framesWritten * WaveFormat.Channels;

            if (playbackEnded)
            {
                _isPlaying = false;
            }
        }

        for (var loop = 0; loop < loopsCompleted; loop++)
        {
            ThreadPool.QueueUserWorkItem(_ => LoopCompleted?.Invoke(this, EventArgs.Empty));
        }

        if (playbackEnded && !_endEventRaised)
        {
            _endEventRaised = true;
            ThreadPool.QueueUserWorkItem(_ => PlaybackEnded?.Invoke(this, EventArgs.Empty));
        }

        return samplesWritten;
    }

    public void Dispose()
    {
        _output.PlaybackStopped -= Output_PlaybackStopped;
        _output.Stop();
        _output.Dispose();

        lock (_gate)
        {
            foreach (var track in _tracks)
            {
                track.Dispose();
            }

            _tracks.Clear();
            _isPlaying = false;
        }
    }

    private void MixTracks(float[] output, int outputOffset, int sampleCount, long timelineFrame)
    {
        if (_trackBuffer.Length < sampleCount)
        {
            _trackBuffer = new float[sampleCount];
        }

        foreach (var track in _tracks)
        {
            if (track.Model.IsMuted || track.Model.Volume <= 0)
            {
                continue;
            }

            Array.Clear(_trackBuffer, 0, sampleCount);
            track.Read(_trackBuffer, 0, sampleCount, timelineFrame);
            var trackVolume = (float)track.Model.Volume;

            for (var sample = 0; sample < sampleCount; sample++)
            {
                output[outputOffset + sample] += _trackBuffer[sample] * trackVolume;
            }
        }
    }

    private void MixMetronome(float[] output, int outputOffset, int frameCount, long timelineFrame)
    {
        if (!_metronomeEnabled || _metronomeVolume <= 0)
        {
            return;
        }

        for (var frame = 0; frame < frameCount; frame++)
        {
            var click = MetronomeSignal.GetSample(
                timelineFrame + frame,
                OutputSampleRate,
                _beatsPerMinute) * _metronomeVolume;
            var sampleOffset = outputOffset + frame * WaveFormat.Channels;
            output[sampleOffset] += click;
            output[sampleOffset + 1] += click;
        }
    }

    private void ApplyMasterVolume(float[] buffer, int offset, int count)
    {
        for (var index = offset; index < offset + count; index++)
        {
            var sample = buffer[index] * _masterVolume;
            buffer[index] = Math.Clamp(sample, -1, 1);
        }
    }

    private void ApplyLoopEdgeFade(
        float[] buffer,
        int offset,
        int frameCount,
        long timelineFrame)
    {
        if (!_clock.IsLoopEnabled)
        {
            return;
        }

        var fadeFrames = Math.Min(
            OutputSampleRate / 200,
            (_clock.LoopEndFrame - _clock.LoopStartFrame) / 4);
        if (fadeFrames <= 0)
        {
            return;
        }

        for (var frame = 0; frame < frameCount; frame++)
        {
            var currentFrame = timelineFrame + frame;
            var framesFromStart = currentFrame - _clock.LoopStartFrame;
            var framesToEnd = _clock.LoopEndFrame - currentFrame;
            var gain = Math.Clamp(
                Math.Min(framesFromStart, framesToEnd) / (float)fadeFrames,
                0,
                1);
            var sampleOffset = offset + frame * WaveFormat.Channels;
            buffer[sampleOffset] *= gain;
            buffer[sampleOffset + 1] *= gain;
        }
    }

    private void RecalculateDuration()
    {
        var duration = _exercisePlayback is not null
            ? _exercisePlayback.Exercise.GetDuration(_beatsPerMinute)
            : _tracks.Count == 0
                ? EmptySessionDuration
                : _tracks.Max(track => track.Model.End);
        _clock.SetDuration(duration);
        SeekTracksToClock();
    }

    private void SeekInternal(TimeSpan position)
    {
        _clock.Seek(position);
        SeekTracksToClock();
    }

    private void SeekTracksToClock()
    {
        if (_exercisePlayback is not null)
        {
            return;
        }

        foreach (var track in _tracks)
        {
            track.Seek(_clock.PositionFrame);
        }
    }

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is null)
        {
            return;
        }

        lock (_gate)
        {
            _isPlaying = false;
        }

        PlaybackFaulted?.Invoke(this, eventArgs);
    }
}
