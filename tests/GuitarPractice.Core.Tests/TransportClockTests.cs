using GuitarPractice.Core;

namespace GuitarPractice.Core.Tests;

public sealed class TransportClockTests
{
    [Fact]
    public void SeekClampsToDuration()
    {
        var clock = new TransportClock(1000, TimeSpan.FromSeconds(10));

        clock.Seek(TimeSpan.FromSeconds(12));

        Assert.Equal(TimeSpan.FromSeconds(10), clock.Position);
    }

    [Fact]
    public void AdvanceWrapsAtLoopEnd()
    {
        var clock = new TransportClock(1000, TimeSpan.FromSeconds(20));
        clock.SetLoopRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8));
        clock.SetLoopEnabled(true);
        clock.Seek(TimeSpan.FromSeconds(7.5));

        var availableFrames = clock.FramesUntilBoundary(1000);
        var result = clock.Advance(availableFrames);

        Assert.Equal(500, availableFrames);
        Assert.Equal(TransportAdvanceResult.Looped, result);
        Assert.Equal(TimeSpan.FromSeconds(4), clock.Position);
    }

    [Fact]
    public void LoopRangeRejectsTinySelection()
    {
        var clock = new TransportClock(48000, TimeSpan.FromSeconds(10));

        Assert.Throws<ArgumentException>(() =>
            clock.SetLoopRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2.01)));
    }

    [Fact]
    public void DurationReductionKeepsLoopValid()
    {
        var clock = new TransportClock(1000, TimeSpan.FromSeconds(20));
        clock.SetLoopRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(18));

        clock.SetDuration(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.Zero, clock.LoopStart);
        Assert.Equal(TimeSpan.FromSeconds(5), clock.LoopEnd);
    }

    [Fact]
    public void FullDurationLoopExpandsWithSession()
    {
        var clock = new TransportClock(1000, TimeSpan.FromSeconds(10));

        clock.SetDuration(TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(20), clock.LoopEnd);
    }

    [Fact]
    public void CustomLoopDoesNotExpandWithSession()
    {
        var clock = new TransportClock(1000, TimeSpan.FromSeconds(10));
        clock.SetLoopRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6));

        clock.SetDuration(TimeSpan.FromSeconds(20));

        Assert.Equal(TimeSpan.FromSeconds(2), clock.LoopStart);
        Assert.Equal(TimeSpan.FromSeconds(6), clock.LoopEnd);
    }
}
