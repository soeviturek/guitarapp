using GuitarPractice.Core;

namespace GuitarPractice.Core.Tests;

public sealed class TimeDisplayTests
{
    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(65.34, "1:05.3")]
    [InlineData(3661.28, "1:01:01.2")]
    public void FormatsPracticeTime(double seconds, string expected)
    {
        Assert.Equal(expected, TimeDisplay.Format(TimeSpan.FromSeconds(seconds)));
    }
}
