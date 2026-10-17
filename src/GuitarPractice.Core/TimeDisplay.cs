using System.Globalization;

namespace GuitarPractice.Core;

public static class TimeDisplay
{
    public static string Format(TimeSpan value)
    {
        var safeValue = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return safeValue.TotalHours >= 1
            ? safeValue.ToString(@"h\:mm\:ss\.f", CultureInfo.InvariantCulture)
            : safeValue.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
    }
}
