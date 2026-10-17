using System.ComponentModel;
using System.Runtime.CompilerServices;
using GuitarPractice.Core;

namespace GuitarPractice.App.Models;

public sealed class AudioTrackModel : INotifyPropertyChanged
{
    private string _name;
    private double _volume;
    private bool _isMuted;
    private float[] _waveformPeaks = [];

    public AudioTrackModel(
        string filePath,
        string name,
        TimeSpan duration,
        TimeSpan offset,
        TrackKind kind,
        double volume = 0.85,
        bool isMuted = false)
    {
        Id = Guid.NewGuid();
        FilePath = filePath;
        _name = name;
        Duration = duration;
        Offset = offset;
        Kind = kind;
        _volume = Math.Clamp(volume, 0, 1);
        _isMuted = isMuted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string FilePath { get; }

    public TrackKind Kind { get; }

    public TimeSpan Duration { get; }

    public TimeSpan Offset { get; }

    public TimeSpan End => Offset + Duration;

    public string DurationDisplay => TimeDisplay.Format(Duration);

    public string KindDisplay => Kind == TrackKind.Recording ? "录音" : "音轨";

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public double Volume
    {
        get => _volume;
        set => SetField(ref _volume, Math.Clamp(value, 0, 1));
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    public IReadOnlyList<float> WaveformPeaks => _waveformPeaks;

    public void SetWaveform(float[] peaks)
    {
        _waveformPeaks = peaks;
        OnPropertyChanged(nameof(WaveformPeaks));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
