using NAudio.Wave;

namespace GuitarPractice.App.Tests;

internal sealed class FakeWavePlayer : IWavePlayer
{
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public float Volume { get; set; } = 1;

    public PlaybackState PlaybackState { get; private set; }

    public WaveFormat OutputWaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public void Init(IWaveProvider waveProvider) => OutputWaveFormat = waveProvider.WaveFormat;

    public void Play() => PlaybackState = PlaybackState.Playing;

    public void Pause() => PlaybackState = PlaybackState.Paused;

    public void Stop()
    {
        PlaybackState = PlaybackState.Stopped;
        PlaybackStopped?.Invoke(this, new StoppedEventArgs());
    }

    public void Dispose()
    {
        PlaybackState = PlaybackState.Stopped;
    }
}
