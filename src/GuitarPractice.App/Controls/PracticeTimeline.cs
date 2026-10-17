using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GuitarPractice.App.Models;

namespace GuitarPractice.App.Controls;

public sealed class PracticeTimeline : FrameworkElement
{
    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(20, 24, 32));
    private static readonly Brush AlternateRowBrush = new SolidColorBrush(Color.FromRgb(25, 30, 40));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(51, 60, 75));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(158, 171, 190));
    private static readonly Brush WaveformBrush = new SolidColorBrush(Color.FromRgb(95, 211, 180));
    private static readonly Brush RecordingWaveformBrush = new SolidColorBrush(Color.FromRgb(246, 155, 82));
    private static readonly Brush PlayheadBrush = new SolidColorBrush(Color.FromRgb(255, 91, 113));
    private static readonly Brush LoopBrush = new SolidColorBrush(Color.FromArgb(48, 72, 139, 255));
    private static readonly Brush SelectionBrush = new SolidColorBrush(Color.FromArgb(60, 246, 155, 82));
    private const double RulerHeight = 28;

    private bool _isDragging;
    private bool _isSelectingLoop;
    private double _selectionStart;
    private double _selectionEnd;

    public event Action<double>? SeekRequested;

    public event Action<double, double>? LoopSelectionRequested;

    public IReadOnlyList<AudioTrackModel> Tracks { get; set; } = [];

    public double DurationSeconds { get; set; } = 1;

    public double PositionSeconds { get; set; }

    public bool IsLoopEnabled { get; set; }

    public double LoopStartSeconds { get; set; }

    public double LoopEndSeconds { get; set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));

        var duration = Math.Max(0.001, DurationSeconds);
        DrawRows(drawingContext);
        DrawTimeGrid(drawingContext, duration);
        DrawWaveforms(drawingContext, duration);
        DrawLoop(drawingContext, duration);
        DrawPlayhead(drawingContext, duration);

        if (Tracks.Count == 0)
        {
            DrawEmptyMessage(drawingContext);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonDown(eventArgs);
        Focus();
        CaptureMouse();
        _isDragging = true;
        _isSelectingLoop = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _selectionStart = TimeFromPoint(eventArgs.GetPosition(this).X);
        _selectionEnd = _selectionStart;

        if (!_isSelectingLoop)
        {
            SeekRequested?.Invoke(_selectionStart);
        }

        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);

        if (!_isDragging || eventArgs.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var time = TimeFromPoint(eventArgs.GetPosition(this).X);
        if (_isSelectingLoop)
        {
            _selectionEnd = time;
            InvalidateVisual();
        }
        else
        {
            SeekRequested?.Invoke(time);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonUp(eventArgs);

        if (!_isDragging)
        {
            return;
        }

        if (_isSelectingLoop)
        {
            _selectionEnd = TimeFromPoint(eventArgs.GetPosition(this).X);
            var start = Math.Min(_selectionStart, _selectionEnd);
            var end = Math.Max(_selectionStart, _selectionEnd);

            if (end - start >= 0.05)
            {
                LoopSelectionRequested?.Invoke(start, end);
            }
        }

        _isDragging = false;
        _isSelectingLoop = false;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    private void DrawRows(DrawingContext drawingContext)
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        var rowHeight = Math.Max(44, (ActualHeight - RulerHeight) / Tracks.Count);
        for (var index = 0; index < Tracks.Count; index++)
        {
            var top = RulerHeight + index * rowHeight;
            if (index % 2 == 1)
            {
                drawingContext.DrawRectangle(
                    AlternateRowBrush,
                    null,
                    new Rect(0, top, ActualWidth, rowHeight));
            }

            drawingContext.DrawLine(
                new Pen(GridBrush, 1),
                new Point(0, top + rowHeight),
                new Point(ActualWidth, top + rowHeight));
        }
    }

    private void DrawTimeGrid(DrawingContext drawingContext, double duration)
    {
        var interval = ChooseGridInterval(duration);
        var pixelsPerSecond = ActualWidth / duration;
        var typeface = new Typeface("Segoe UI");
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var second = 0.0; second <= duration; second += interval)
        {
            var x = second * pixelsPerSecond;
            drawingContext.DrawLine(
                new Pen(GridBrush, 1),
                new Point(x, RulerHeight),
                new Point(x, ActualHeight));

            var label = FormatRulerTime(second);
            var text = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                TextBrush,
                pixelsPerDip);
            drawingContext.DrawText(text, new Point(Math.Min(x + 4, ActualWidth - text.Width - 4), 6));
        }

        drawingContext.DrawLine(
            new Pen(GridBrush, 1),
            new Point(0, RulerHeight),
            new Point(ActualWidth, RulerHeight));
    }

    private void DrawWaveforms(DrawingContext drawingContext, double duration)
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        var rowHeight = Math.Max(44, (ActualHeight - RulerHeight) / Tracks.Count);

        for (var trackIndex = 0; trackIndex < Tracks.Count; trackIndex++)
        {
            var track = Tracks[trackIndex];
            var peaks = track.WaveformPeaks;
            if (peaks.Count == 0)
            {
                continue;
            }

            var centerY = RulerHeight + trackIndex * rowHeight + rowHeight / 2;
            var maxAmplitude = rowHeight * 0.34;
            var pen = new Pen(
                track.Kind == TrackKind.Recording ? RecordingWaveformBrush : WaveformBrush,
                1);

            for (var index = 0; index < peaks.Count; index++)
            {
                var progress = peaks.Count == 1 ? 0 : (double)index / (peaks.Count - 1);
                var timelineSecond = track.Offset.TotalSeconds + track.Duration.TotalSeconds * progress;
                var x = timelineSecond / duration * ActualWidth;
                var amplitude = Math.Clamp(peaks[index], 0, 1) * maxAmplitude;
                drawingContext.DrawLine(
                    pen,
                    new Point(x, centerY - amplitude),
                    new Point(x, centerY + amplitude));
            }
        }
    }

    private void DrawLoop(DrawingContext drawingContext, double duration)
    {
        if (IsLoopEnabled && LoopEndSeconds > LoopStartSeconds)
        {
            DrawRange(
                drawingContext,
                LoopStartSeconds,
                LoopEndSeconds,
                duration,
                LoopBrush,
                new Pen(new SolidColorBrush(Color.FromRgb(72, 139, 255)), 2));
        }

        if (_isDragging && _isSelectingLoop)
        {
            DrawRange(
                drawingContext,
                Math.Min(_selectionStart, _selectionEnd),
                Math.Max(_selectionStart, _selectionEnd),
                duration,
                SelectionBrush,
                new Pen(RecordingWaveformBrush, 2));
        }
    }

    private void DrawRange(
        DrawingContext drawingContext,
        double start,
        double end,
        double duration,
        Brush fill,
        Pen edge)
    {
        var startX = Math.Clamp(start / duration * ActualWidth, 0, ActualWidth);
        var endX = Math.Clamp(end / duration * ActualWidth, 0, ActualWidth);
        drawingContext.DrawRectangle(
            fill,
            null,
            new Rect(startX, RulerHeight, Math.Max(0, endX - startX), ActualHeight - RulerHeight));
        drawingContext.DrawLine(edge, new Point(startX, RulerHeight), new Point(startX, ActualHeight));
        drawingContext.DrawLine(edge, new Point(endX, RulerHeight), new Point(endX, ActualHeight));
    }

    private void DrawPlayhead(DrawingContext drawingContext, double duration)
    {
        var x = Math.Clamp(PositionSeconds / duration * ActualWidth, 0, ActualWidth);
        var pen = new Pen(PlayheadBrush, 2);
        drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));

        var marker = new StreamGeometry();
        using (var context = marker.Open())
        {
            context.BeginFigure(new Point(x - 5, 0), true, true);
            context.LineTo(new Point(x + 5, 0), true, false);
            context.LineTo(new Point(x, 8), true, false);
        }

        marker.Freeze();
        drawingContext.DrawGeometry(PlayheadBrush, null, marker);
    }

    private void DrawEmptyMessage(DrawingContext drawingContext)
    {
        var text = new FormattedText(
            "导入音频，或直接把文件拖到这里",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            18,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(
            text,
            new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
    }

    private double TimeFromPoint(double x)
    {
        return Math.Clamp(x / Math.Max(1, ActualWidth) * DurationSeconds, 0, DurationSeconds);
    }

    private double ChooseGridInterval(double duration)
    {
        var targetInterval = duration / Math.Max(1, ActualWidth / 90);
        double[] intervals = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600];
        return intervals.FirstOrDefault(interval => interval >= targetInterval, intervals[^1]);
    }

    private static string FormatRulerTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
