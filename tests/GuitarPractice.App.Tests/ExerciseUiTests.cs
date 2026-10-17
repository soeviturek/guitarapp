using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuitarPractice.App.Audio;
using GuitarPractice.App.Controls;
using GuitarPractice.App.Persistence;
using GuitarPractice.Core;

namespace GuitarPractice.App.Tests;

public sealed class ExerciseUiTests
{
    [Fact]
    public void ModeSwitchingAndSavingPreserveTheAudioWorkspace()
    {
        RunOnSta(() =>
        {
            using var engine = new PracticeAudioEngine(new FakeWavePlayer());
            var window = new MainWindow(engine);
            var bpm = Assert.IsType<Slider>(window.FindName("BpmSlider"));
            var toggle = Assert.IsType<ToggleButton>(window.FindName("ExerciseModeToggle"));
            bpm.Value = 123;
            engine.SetLoopRange(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8));
            engine.SetLoopEnabled(true);
            engine.Seek(TimeSpan.FromSeconds(5));

            toggle.IsChecked = true;
            bpm.Value = 60;
            var saved = window.CreateSessionSnapshot();
            var exercise = Assert.IsType<ExerciseSessionState>(saved.Exercise);

            Assert.Equal(123, saved.BeatsPerMinute);
            Assert.Equal(4, saved.LoopStartSeconds);
            Assert.Equal(8, saved.LoopEndSeconds);
            Assert.True(saved.LoopEnabled);
            Assert.True(exercise.IsActive);
            Assert.Equal(60, exercise.BeatsPerMinute);

            toggle.IsChecked = false;

            Assert.Equal(123, engine.BeatsPerMinute);
            Assert.Equal(TimeSpan.FromSeconds(5), engine.Position);
            Assert.Equal(TimeSpan.FromSeconds(4), engine.LoopStart);
            Assert.Equal(TimeSpan.FromSeconds(8), engine.LoopEnd);
            Assert.True(engine.IsLoopEnabled);
            Assert.Equal(Visibility.Visible,
                Assert.IsType<Grid>(window.FindName("AudioWorkspace")).Visibility);
            Assert.Equal(Visibility.Collapsed,
                Assert.IsType<Grid>(window.FindName("ExerciseWorkspace")).Visibility);
        });
    }

    [Fact]
    public void BpmAndMultiplierControlsAreLinkedInBothDirections()
    {
        RunOnSta(() =>
        {
            using var engine = new PracticeAudioEngine(new FakeWavePlayer());
            var window = new MainWindow(engine);
            Assert.IsType<ToggleButton>(window.FindName("ExerciseModeToggle")).IsChecked = true;
            var bpm = Assert.IsType<Slider>(window.FindName("BpmSlider"));
            var speed = Assert.IsType<Slider>(window.FindName("ExerciseSpeedSlider"));

            bpm.Value = 60;
            Assert.Equal(0.5, speed.Value, 8);
            Assert.Equal(ExerciseLibrary.All[0].GetDuration(60), engine.Duration);

            speed.Value = 1.25;
            Assert.Equal(150, bpm.Value);
            Assert.Equal(150, engine.BeatsPerMinute);
            Assert.Equal(ExerciseLibrary.All[0].GetDuration(150), engine.Duration);
            Assert.False(Assert.IsType<ToggleButton>(window.FindName("RecordArmToggle")).IsEnabled);
        });
    }

    [Fact]
    public void PlayingAfterStopStartsInsideTheSelectedExerciseLoop()
    {
        RunOnSta(() =>
        {
            using var engine = new PracticeAudioEngine(new FakeWavePlayer());
            var window = new MainWindow(engine);
            Assert.IsType<ToggleButton>(window.FindName("ExerciseModeToggle")).IsChecked = true;
            engine.SetLoopRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            engine.Stop();

            Assert.IsType<Button>(window.FindName("PlayButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(engine.IsPlaying);
            Assert.Equal(TimeSpan.FromSeconds(1), engine.Position);
        });
    }

    [Fact]
    public void SelectingAPresetUpdatesAudioScoreAndSessionTogether()
    {
        RunOnSta(() =>
        {
            using var engine = new PracticeAudioEngine(new FakeWavePlayer());
            var window = new MainWindow(engine);
            Assert.IsType<ToggleButton>(window.FindName("ExerciseModeToggle")).IsChecked = true;
            engine.SetLoopRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
            var preset = ExerciseLibrary.All.Single(exercise => exercise.Id == "a-minor-pentatonic");

            Assert.IsType<ComboBox>(window.FindName("ExercisePresetCombo")).SelectedItem = preset;

            Assert.Same(preset, Assert.IsType<ExerciseTablature>(window.FindName("Tablature")).Exercise);
            Assert.Equal(preset.GetDuration(80), engine.Duration);
            Assert.Equal(TimeSpan.Zero, engine.Position);
            Assert.Equal(TimeSpan.Zero, engine.LoopStart);
            Assert.Equal(engine.Duration, engine.LoopEnd);
            Assert.True(engine.IsLoopEnabled);
            Assert.Equal(preset.Id, Assert.IsType<ExerciseSessionState>(window.CreateSessionSnapshot().Exercise).PresetId);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RestoringExerciseSettingsLoadsPresetTempoSoundAndBeatLoop(bool active)
    {
        RunOnSta(() =>
        {
            using var engine = new PracticeAudioEngine(new FakeWavePlayer());
            var window = new MainWindow(engine);
            Assert.IsType<Slider>(window.FindName("BpmSlider")).Value = 123;
            var state = new ExerciseSessionState
            {
                IsActive = active,
                PresetId = "c-major",
                BeatsPerMinute = 60,
                SoundEnabled = false,
                Volume = 0.4,
                FollowPlayback = false,
                LoopEnabled = true,
                LoopStartBeats = 4,
                LoopEndBeats = 8
            };

            window.RestoreExerciseState(state);

            Assert.Equal(active, Assert.IsType<ToggleButton>(window.FindName("ExerciseModeToggle")).IsChecked);
            Assert.False(engine.ExerciseSoundEnabled);
            Assert.Equal(0.4f, engine.ExerciseVolume);
            Assert.False(Assert.IsType<ToggleButton>(window.FindName("ExerciseFollowToggle")).IsChecked);
            Assert.Equal("c-major", Assert.IsType<ExerciseTablature>(window.FindName("Tablature")).Exercise?.Id);
            Assert.Equal(active ? 60 : 123, engine.BeatsPerMinute);
            if (active)
            {
                Assert.Equal(TimeSpan.FromSeconds(4), engine.Position);
                Assert.Equal(TimeSpan.FromSeconds(4), engine.LoopStart);
                Assert.Equal(TimeSpan.FromSeconds(8), engine.LoopEnd);
                Assert.True(engine.IsLoopEnabled);
            }
            Assert.Equal(123, window.CreateSessionSnapshot().BeatsPerMinute);
        });
    }

    [Fact]
    public void TablatureReflowsAndHitTestingMatchesDisplayedMeasures()
    {
        RunOnSta(() =>
        {
            var score = new ExerciseTablature { Exercise = ExerciseLibrary.All[1] };
            score.Measure(new Size(640, double.PositiveInfinity));
            var narrowHeight = score.DesiredSize.Height;
            score.Arrange(new Rect(0, 0, 640, narrowHeight));
            Assert.Equal(0, score.GetStepFromPoint(new Point(62, 100)));
            Assert.Equal(8, score.GetStepFromPoint(new Point(62, 278)));

            score.Measure(new Size(1000, double.PositiveInfinity));
            score.Arrange(new Rect(0, 0, 1000, score.DesiredSize.Height));

            Assert.True(score.DesiredSize.Height < narrowHeight);
            Assert.Equal(8, score.GetStepFromPoint(new Point(550, 100)));
            Assert.Equal(16, score.GetStepFromPoint(new Point(62, 278)));
            Assert.Equal(score.Exercise.StepCount - 1,
                score.GetStepFromPoint(new Point(10000, 10000)));
        });
    }

    [Fact]
    public void RenderedHighlightFollowsThePlayingNoteAndClearsForRests()
    {
        RunOnSta(() =>
        {
            var exercise = new PracticeExercise("render", "Render", "Render",
                [new ExerciseMeasure("C", [new(5, 3), null, null, null, null, null, null, null])]);
            var score = new ExerciseTablature { Exercise = exercise };
            score.Measure(new Size(640, double.PositiveInfinity));
            score.Arrange(new Rect(0, 0, 640, score.DesiredSize.Height));

            score.UpdatePlayback(TimeSpan.Zero, 120, false, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.Equal(Color.FromRgb(95, 211, 180), GetPixel(score, 50, 116));

            score.UpdatePlayback(TimeSpan.FromSeconds(0.25), 120, false, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            Assert.Equal(Color.FromRgb(25, 30, 40), GetPixel(score, 50, 116));
        });
    }

    [Fact]
    public void LegacyAndExerciseSessionFormatsRemainCompatible()
    {
        var legacy = JsonSerializer.Deserialize<PracticeSession>("""{"BeatsPerMinute":92,"Tracks":[]}""");
        Assert.NotNull(legacy);
        Assert.Equal(92, legacy.BeatsPerMinute);
        Assert.Null(legacy.Exercise);

        var session = new PracticeSession
        {
            BeatsPerMinute = 123,
            Exercise = new ExerciseSessionState
            {
                IsActive = true,
                PresetId = "c-major",
                BeatsPerMinute = 60,
                LoopStartBeats = 4,
                LoopEndBeats = 8
            }
        };
        var restored = JsonSerializer.Deserialize<PracticeSession>(JsonSerializer.Serialize(session));
        Assert.NotNull(restored);
        Assert.Equal(123, restored.BeatsPerMinute);
        var exercise = Assert.IsType<ExerciseSessionState>(restored.Exercise);
        Assert.Equal("c-major", exercise.PresetId);
        Assert.Equal(4, exercise.LoopStartBeats);
        Assert.Equal(8, exercise.LoopEndBeats);
    }

    private static Color GetPixel(ExerciseTablature score, int x, int y)
    {
        score.UpdateLayout();
        var bitmap = new RenderTargetBitmap(640, 202, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(score);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The STA UI check did not finish.");
        failure?.Throw();
    }
}
