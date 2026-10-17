using NAudio.Wave;

namespace GuitarPractice.App.Audio;

public static class WaveformExtractor
{
    public static Task<float[]> ExtractAsync(
        string filePath,
        int pointCount = 1600,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Extract(filePath, pointCount, cancellationToken), cancellationToken);
    }

    private static float[] Extract(
        string filePath,
        int pointCount,
        CancellationToken cancellationToken)
    {
        using var reader = new AudioFileReader(filePath);
        var totalSamples = Math.Max(1, reader.Length / sizeof(float));
        var samplesPerPoint = Math.Max(reader.WaveFormat.Channels, totalSamples / pointCount);
        var peaks = new float[pointCount];
        var buffer = new float[8192];
        var pointIndex = 0;
        long samplesInPoint = 0;
        var peak = 0f;

        int samplesRead;
        while (pointIndex < pointCount && (samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var sample = 0; sample < samplesRead && pointIndex < pointCount; sample++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[sample]));
                samplesInPoint++;

                if (samplesInPoint < samplesPerPoint)
                {
                    continue;
                }

                peaks[pointIndex++] = peak;
                samplesInPoint = 0;
                peak = 0;
            }
        }

        if (samplesInPoint > 0 && pointIndex < pointCount)
        {
            peaks[pointIndex++] = peak;
        }

        return pointIndex == peaks.Length ? peaks : peaks[..Math.Max(1, pointIndex)];
    }
}
