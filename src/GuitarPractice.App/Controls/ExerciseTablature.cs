using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GuitarPractice.App.Audio;
using GuitarPractice.Core;

namespace GuitarPractice.App.Controls;

public sealed class ExerciseTablature : FrameworkElement
{
    private const double MarginSize = 12;
    private const double RowHeight = 178;
    private static readonly Brush BackgroundBrush = CreateBrush(Color.FromRgb(20, 24, 32));
    private static readonly Brush MeasureBrush = CreateBrush(Color.FromRgb(25, 30, 40));
    private static readonly Brush GridBrush = CreateBrush(Color.FromRgb(66, 77, 94));
    private static readonly Brush TextBrush = CreateBrush(Color.FromRgb(237, 242, 247));
    private static readonly Brush MutedBrush = CreateBrush(Color.FromRgb(158, 171, 190));
    private static readonly Brush AccentBrush = CreateBrush(Color.FromRgb(95, 211, 180));
    private static readonly Brush PlayheadBrush = CreateBrush(Color.FromRgb(255, 91, 113));
    private static readonly Brush LoopBrush = CreateBrush(Color.FromArgb(45, 72, 139, 255));
    private static readonly Brush SelectionBrush = CreateBrush(Color.FromArgb(65, 246, 155, 82));
    private static readonly string[] StringNames = ["e", "B", "G", "D", "A", "E"];

    private PracticeExercise? _exercise;
    private double _bpm = 120;
    private long _positionFrame;
    private double _loopStartSeconds;
    private double _loopEndSeconds;
    private bool _loopEnabled;
    private bool _isDragging;
    private bool _isSelectingLoop;
    private int _selectionStart;
    private int _selectionEnd;
    private int _lastFollowedRow = -1;
    private ScrollViewer? _scrollViewer;
    private bool _followPlayback = true;

    public ExerciseTablature()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        Loaded += Tablature_Loaded;
        Unloaded += Tablature_Unloaded;
    }

    public event Action<double>? SeekRequested;

    public event Action<double, double>? LoopSelectionRequested;

    public bool FollowPlayback
    {
        get => _followPlayback;
        set
        {
            if (value && !_followPlayback)
            {
                _lastFollowedRow = -1;
            }
            _followPlayback = value;
        }
    }

    public PracticeExercise? Exercise
    {
        get => _exercise;
        set
        {
            _exercise = value;
            _lastFollowedRow = -1;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private int Columns => Math.Clamp((int)((ActualWidth - MarginSize * 2) / 420), 1, 2);

    public void UpdatePlayback(
        TimeSpan position,
        double beatsPerMinute,
        bool loopEnabled,
        TimeSpan loopStart,
        TimeSpan loopEnd)
    {
        _positionFrame = (long)Math.Round(position.TotalSeconds * PracticeAudioEngine.OutputSampleRate);
        _bpm = beatsPerMinute;
        _loopEnabled = loopEnabled;
        _loopStartSeconds = loopStart.TotalSeconds;
        _loopEndSeconds = loopEnd.TotalSeconds;
        InvalidateVisual();

        if (Exercise is not null && FollowPlayback && ActualWidth > 0)
        {
            var step = Exercise.GetStepAtFrame(_positionFrame, PracticeAudioEngine.OutputSampleRate, _bpm);
            var measure = Math.Min(Exercise.Measures.Count - 1, step / ExerciseMeasure.StepsPerMeasure);
            var row = measure / Columns;
            if (row != _lastFollowedRow)
            {
                BringIntoView(GetMeasureBounds(measure));
                _lastFollowedRow = row;
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width) : 640;
        var columns = Math.Clamp((int)((width - MarginSize * 2) / 420), 1, 2);
        var rows = (int)Math.Ceiling((Exercise?.Measures.Count ?? 1) / (double)columns);
        return new Size(width, rows * RowHeight + MarginSize * 2);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _lastFollowedRow = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));
        if (Exercise is null)
        {
            return;
        }

        var activeStep = Exercise.GetStepAtFrame(
            _positionFrame, PracticeAudioEngine.OutputSampleRate, _bpm);
        var firstRow = Math.Max(0, (int)((_scrollViewer?.VerticalOffset ?? 0) / RowHeight) - 1);
        var lastRow = _scrollViewer is null
            ? (int)Math.Ceiling(Exercise.Measures.Count / (double)Columns)
            : (int)Math.Ceiling((_scrollViewer.VerticalOffset + _scrollViewer.ViewportHeight) / RowHeight) + 1;
        var lastMeasure = Math.Min(Exercise.Measures.Count, (lastRow + 1) * Columns);
        for (var measure = firstRow * Columns; measure < lastMeasure; measure++)
        {
            DrawMeasure(drawingContext, measure, activeStep);
        }
    }

    private void Tablature_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer scrollViewer)
            {
                _scrollViewer = scrollViewer;
                _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
                InvalidateVisual();
                break;
            }
        }
    }

    private void Tablature_Unloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            _scrollViewer = null;
        }
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
    {
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonDown(eventArgs);
        if (Exercise is null)
        {
            return;
        }

        Focus();
        CaptureMouse();
        _isDragging = true;
        _isSelectingLoop = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _selectionStart = GetStepFromPoint(eventArgs.GetPosition(this));
        _selectionEnd = _selectionStart;
        if (!_isSelectingLoop)
        {
            SeekRequested?.Invoke(Exercise.GetStepTime(_selectionStart, _bpm).TotalSeconds);
        }
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        if (!_isDragging || eventArgs.LeftButton != MouseButtonState.Pressed || Exercise is null)
        {
            return;
        }

        _selectionEnd = GetStepFromPoint(eventArgs.GetPosition(this));
        if (!_isSelectingLoop)
        {
            SeekRequested?.Invoke(Exercise.GetStepTime(_selectionEnd, _bpm).TotalSeconds);
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonUp(eventArgs);
        if (!_isDragging || Exercise is null)
        {
            return;
        }

        _selectionEnd = GetStepFromPoint(eventArgs.GetPosition(this));
        if (_isSelectingLoop)
        {
            var start = Math.Min(_selectionStart, _selectionEnd);
            var end = Math.Max(_selectionStart, _selectionEnd) + 1;
            LoopSelectionRequested?.Invoke(
                Exercise.GetStepTime(start, _bpm).TotalSeconds,
                Exercise.GetStepTime(end, _bpm).TotalSeconds);
        }

        _isDragging = false;
        _isSelectingLoop = false;
        ReleaseMouseCapture();
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs eventArgs)
    {
        base.OnLostMouseCapture(eventArgs);
        _isDragging = false;
        _isSelectingLoop = false;
        InvalidateVisual();
    }

    private void DrawMeasure(DrawingContext context, int measureIndex, int activeStep)
    {
        var exercise = Exercise!;
        var bounds = GetMeasureBounds(measureIndex);
        var measure = exercise.Measures[measureIndex];
        context.DrawRoundedRectangle(MeasureBrush, null, bounds, 6, 6);
        DrawText(context, $"{measureIndex + 1}  {measure.Label}", bounds.Left + 10, bounds.Top + 8, MutedBrush, 12);

        var stepWidth = GetStepWidth(bounds);
        for (var slot = 0; slot < ExerciseMeasure.StepsPerMeasure; slot++)
        {
            var step = measureIndex * ExerciseMeasure.StepsPerMeasure + slot;
            var x = GetStepX(bounds, slot);
            var time = exercise.GetStepTime(step, _bpm).TotalSeconds;
            var selecting = _isDragging && _isSelectingLoop &&
                step >= Math.Min(_selectionStart, _selectionEnd) &&
                step <= Math.Max(_selectionStart, _selectionEnd);
            if (selecting || (_loopEnabled && time >= _loopStartSeconds && time < _loopEndSeconds))
            {
                context.DrawRectangle(
                    selecting ? SelectionBrush : LoopBrush,
                    null,
                    new Rect(x - stepWidth / 2, bounds.Top + 32, stepWidth, 104));
            }
        }

        for (var stringIndex = 0; stringIndex < 6; stringIndex++)
        {
            var y = bounds.Top + 42 + stringIndex * 16;
            DrawText(context, StringNames[stringIndex], bounds.Left + 10, y - 8, MutedBrush, 12);
            context.DrawLine(new Pen(GridBrush, 1), new Point(bounds.Left + 30, y), new Point(bounds.Right - 10, y));
        }

        for (var slot = 0; slot < ExerciseMeasure.StepsPerMeasure; slot++)
        {
            var step = measureIndex * ExerciseMeasure.StepsPerMeasure + slot;
            var x = GetStepX(bounds, slot);
            var note = measure.Notes[slot];
            if (note is not null)
            {
                var y = bounds.Top + 42 + (note.StringNumber - 1) * 16;
                var current = step == activeStep;
                context.DrawRoundedRectangle(
                    current ? AccentBrush : MeasureBrush,
                    null,
                    new Rect(x - 15, y - 11, 30, 22),
                    4, 4);
                DrawText(context, note.Fret.ToString(CultureInfo.InvariantCulture),
                    x, y - 10, current ? BackgroundBrush : TextBrush, 15, true);
            }
            else
            {
                DrawText(context, "r", x, bounds.Top + 72, MutedBrush, 13, true);
            }
            DrawText(context, slot % 2 == 0 ? (slot / 2 + 1).ToString(CultureInfo.InvariantCulture) : "&",
                x, bounds.Top + 141, MutedBrush, 11, true);
        }

        if (activeStep / ExerciseMeasure.StepsPerMeasure == measureIndex && activeStep < exercise.StepCount)
        {
            var start = exercise.GetStepFrame(activeStep, PracticeAudioEngine.OutputSampleRate, _bpm);
            var end = exercise.GetStepFrame(activeStep + 1, PracticeAudioEngine.OutputSampleRate, _bpm);
            var progress = (_positionFrame - start) / (double)(end - start);
            var x = GetStepX(bounds, activeStep % ExerciseMeasure.StepsPerMeasure) + progress * stepWidth;
            context.DrawLine(new Pen(PlayheadBrush, 1.5),
                new Point(x, bounds.Top + 29), new Point(x, bounds.Top + 135));
        }
    }

    private Rect GetMeasureBounds(int measure)
    {
        var width = Math.Max(100, ActualWidth - MarginSize * 2) / Columns;
        return new Rect(
            MarginSize + measure % Columns * width,
            MarginSize + measure / Columns * RowHeight,
            width - 8,
            RowHeight - 10);
    }

    private static double GetStepX(Rect bounds, int slot)
    {
        return bounds.Left + 50 + slot * GetStepWidth(bounds);
    }

    private static double GetStepWidth(Rect bounds)
    {
        return (bounds.Width - 80) / ExerciseMeasure.StepsPerMeasure;
    }

    internal int GetStepFromPoint(Point point)
    {
        var exercise = Exercise!;
        var width = Math.Max(100, ActualWidth - MarginSize * 2) / Columns;
        var column = Math.Clamp((int)((point.X - MarginSize) / width), 0, Columns - 1);
        var row = Math.Max(0, (int)((point.Y - MarginSize) / RowHeight));
        var measure = Math.Min(exercise.Measures.Count - 1, row * Columns + column);
        var bounds = GetMeasureBounds(measure);
        var stepWidth = GetStepWidth(bounds);
        var slot = Math.Clamp((int)Math.Round((point.X - GetStepX(bounds, 0)) / stepWidth), 0, 7);
        return measure * ExerciseMeasure.StepsPerMeasure + slot;
    }

    private void DrawText(
        DrawingContext context, string value, double x, double y, Brush brush, double size, bool centered = false)
    {
        var text = new FormattedText(
            value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new Point(centered ? x - text.Width / 2 : x, y));
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
