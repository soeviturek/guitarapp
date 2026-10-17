using NAudio.Wave;

namespace GuitarPractice.App.Audio;

internal sealed class StereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _sourceBuffer = [];

    public StereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;

        if (_sourceChannels <= 0)
        {
            throw new ArgumentException("The audio source has no channels.", nameof(source));
        }

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var framesRequested = count / 2;
        var sourceSamplesRequested = framesRequested * _sourceChannels;

        if (_sourceBuffer.Length < sourceSamplesRequested)
        {
            _sourceBuffer = new float[sourceSamplesRequested];
        }

        var sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesRequested);
        var framesRead = sourceSamplesRead / _sourceChannels;

        for (var frame = 0; frame < framesRead; frame++)
        {
            var sourceOffset = frame * _sourceChannels;
            var targetOffset = offset + frame * 2;

            if (_sourceChannels == 1)
            {
                buffer[targetOffset] = _sourceBuffer[sourceOffset];
                buffer[targetOffset + 1] = _sourceBuffer[sourceOffset];
            }
            else
            {
                buffer[targetOffset] = _sourceBuffer[sourceOffset];
                buffer[targetOffset + 1] = _sourceBuffer[sourceOffset + 1];
            }
        }

        return framesRead * 2;
    }
}
