using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GuitarPractice.App.Audio;
using GuitarPractice.App.Models;
using GuitarPractice.App.Persistence;
using GuitarPractice.App.Recording;
using GuitarPractice.Core;
using Microsoft.Win32;
using NAudio.Wave;

namespace GuitarPractice.App;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions = new(
        [".wav", ".mp3", ".aiff", ".aif", ".wma", ".m4a", ".aac", ".flac"],
        StringComparer.OrdinalIgnoreCase);

    private readonly ObservableCollection<AudioTrackModel> _tracks = [];
    private readonly AudioRecorder _recorder = new();
    private readonly SessionStore _sessionStore = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private PracticeAudioEngine? _engine;
    private bool _isUpdatingUi;
    private bool _isDraggingPosition;
    private bool _sessionReady;
    private bool _closeAfterSave;
    private bool _resourcesDisposed;
    private int _loopRecordingFinishQueued;
    private Task? _finishRecordingTask;
    private TimeSpan _recordingStart;

    public MainWindow()
    {
        InitializeComponent();

        TrackItems.ItemsSource = _tracks;
        Timeline.Tracks = _tracks;
        Timeline.SeekRequested += Timeline_SeekRequested;
        Timeline.LoopSelectionRequested += Timeline_LoopSelectionRequested;

        _uiTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _uiTimer.Tick += UiTimer_Tick;

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _saveTimer.Tick += SaveTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        try
        {
            _engine = new PracticeAudioEngine();
            _engine.PlaybackEnded += Engine_PlaybackEnded;
            _engine.LoopCompleted += Engine_LoopCompleted;
            _engine.PlaybackFaulted += Engine_PlaybackFaulted;
            ApplyControlsToEngine();
        }
        catch (Exception exception)
        {
            MainContent.IsEnabled = false;
            SetStatus("无法初始化 Windows 音频输出。");
            ShowError("音频输出初始化失败", exception);
            return;
        }

        LoadRecordingInputs();

        try
        {
            await RestoreSessionAsync();
        }
        catch (JsonException exception)
        {
            ShowError("上次会话文件已损坏，本次将从空白会话开始", exception);
        }
        catch (IOException exception)
        {
            ShowError("无法读取上次会话", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            ShowError("没有权限读取上次会话", exception);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        _sessionReady = true;
        _uiTimer.Start();
        UpdateUi();
        SetStatus(_tracks.Count == 0
            ? "准备就绪。请导入音频，或只开启节拍器开始练习。"
            : $"已恢复 {_tracks.Count} 条音轨。");
    }

    private async void ImportTracks_Click(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入练习音轨",
            Multiselect = true,
            Filter = "音频文件|*.wav;*.mp3;*.aiff;*.aif;*.wma;*.m4a;*.aac;*.flac|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await ImportFilesAsync(dialog.FileNames);
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            if (_engine.IsPlaying)
            {
                _engine.Pause();
                await FinishRecordingAsync();
            }
            else
            {
                if (RecordArmToggle.IsChecked == true &&
                    !_recorder.IsRecording &&
                    !StartRecording())
                {
                    return;
                }

                _engine.Play();
            }

            UpdateUi();
        }
        catch (Exception exception)
        {
            ShowError("无法切换播放状态", exception);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            _engine.Stop();
            await FinishRecordingAsync();
            UpdateUi();
        }
        catch (Exception exception)
        {
            ShowError("停止播放时发生错误", exception);
        }
    }

    private void SetLoopStart_Click(object sender, RoutedEventArgs eventArgs)
    {
        SetLoopStartAtCurrentPosition();
    }

    private void SetLoopEnd_Click(object sender, RoutedEventArgs eventArgs)
    {
        SetLoopEndAtCurrentPosition();
    }

    private void LoopToggle_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (_isUpdatingUi || _engine is null)
        {
            return;
        }

        _engine.SetLoopEnabled(LoopToggle.IsChecked == true);
        ScheduleSessionSave();
        UpdateUi();
    }

    private void MetronomeToggle_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (_isUpdatingUi || _engine is null)
        {
            return;
        }

        _engine.MetronomeEnabled = MetronomeToggle.IsChecked == true;
        ScheduleSessionSave();
    }

    private void BpmSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (BpmLabel is null)
        {
            return;
        }

        BpmLabel.Text = $"{Math.Round(eventArgs.NewValue):0} BPM";
        if (_isUpdatingUi || _engine is null)
        {
            return;
        }

        _engine.BeatsPerMinute = Math.Round(eventArgs.NewValue);
        ScheduleSessionSave();
    }

    private void MetronomeVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (_isUpdatingUi || _engine is null)
        {
            return;
        }

        _engine.MetronomeVolume = (float)eventArgs.NewValue;
        ScheduleSessionSave();
    }

    private void MasterVolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (MasterVolumeLabel is not null)
        {
            MasterVolumeLabel.Text = $"{eventArgs.NewValue:P0}";
        }

        if (_isUpdatingUi || _engine is null)
        {
            return;
        }

        _engine.MasterVolume = (float)eventArgs.NewValue;
        ScheduleSessionSave();
    }

    private void RecordArmToggle_Changed(object sender, RoutedEventArgs eventArgs)
    {
        if (RecordArmToggle is null || RecordingHint is null)
        {
            return;
        }

        var isArmed = RecordArmToggle.IsChecked == true;
        RecordArmToggle.Content = isArmed ? "● 已待命" : "○ 待命";
        RecordingHint.Text = _recorder.IsRecording
            ? "正在录音。暂停、停止或完成一次循环后会自动生成新音轨。"
            : isArmed
                ? "已待命：按播放后开始录制吉他输入。"
                : "勾选待命后按播放，录音会在暂停或停止时自动作为新音轨加入。";
    }

    private void RecordingInputCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        ScheduleSessionSave();
    }

    private void PositionSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> eventArgs)
    {
        if (_isUpdatingUi || !_isDraggingPosition || _engine is null)
        {
            return;
        }

        _engine.Seek(TimeSpan.FromSeconds(eventArgs.NewValue));
        UpdateUi();
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        _isDraggingPosition = true;
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        if (_engine is not null)
        {
            _engine.Seek(TimeSpan.FromSeconds(PositionSlider.Value));
        }

        _isDraggingPosition = false;
        UpdateUi();
    }

    private void RemoveTrack_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: AudioTrackModel track } || _engine is null)
        {
            return;
        }

        track.PropertyChanged -= Track_PropertyChanged;
        _engine.RemoveTrack(track.Id);
        _tracks.Remove(track);
        RefreshTrackDisplay();
        ScheduleSessionSave();
        SetStatus($"已移除“{track.Name}”。");
    }

    private void Window_DragOver(object sender, DragEventArgs eventArgs)
    {
        eventArgs.Effects = GetSupportedDroppedFiles(eventArgs.Data).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs eventArgs)
    {
        var files = GetSupportedDroppedFiles(eventArgs.Data);
        if (files.Count > 0)
        {
            await ImportFilesAsync(files);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.OriginalSource is TextBox or ComboBox)
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.Space:
                PlayPause_Click(this, new RoutedEventArgs());
                eventArgs.Handled = true;
                break;
            case Key.OemOpenBrackets:
                SetLoopStartAtCurrentPosition();
                eventArgs.Handled = true;
                break;
            case Key.OemCloseBrackets:
                SetLoopEndAtCurrentPosition();
                eventArgs.Handled = true;
                break;
            case Key.R:
                RecordArmToggle.IsChecked = RecordArmToggle.IsChecked != true;
                eventArgs.Handled = true;
                break;
        }
    }

    private async void SaveTimer_Tick(object? sender, EventArgs eventArgs)
    {
        _saveTimer.Stop();

        try
        {
            await SaveSessionAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"自动保存失败：{exception.Message}");
        }
    }

    private void UiTimer_Tick(object? sender, EventArgs eventArgs)
    {
        UpdateUi();
    }

    private void Timeline_SeekRequested(double seconds)
    {
        _engine?.Seek(TimeSpan.FromSeconds(seconds));
        UpdateUi();
    }

    private void Timeline_LoopSelectionRequested(double startSeconds, double endSeconds)
    {
        if (_engine is null)
        {
            return;
        }

        _engine.SetLoopRange(
            TimeSpan.FromSeconds(startSeconds),
            TimeSpan.FromSeconds(endSeconds));
        _engine.SetLoopEnabled(true);

        _isUpdatingUi = true;
        LoopToggle.IsChecked = true;
        _isUpdatingUi = false;

        UpdateUi();
        ScheduleSessionSave();
        SetStatus("已从时间轴选择循环区间。");
    }

    private void Engine_PlaybackEnded(object? sender, EventArgs eventArgs)
    {
        Dispatcher.BeginInvoke(new Action(() => _ = HandlePlaybackEndedAsync()));
    }

    private void Engine_LoopCompleted(object? sender, EventArgs eventArgs)
    {
        if (!_recorder.IsRecording ||
            Interlocked.CompareExchange(ref _loopRecordingFinishQueued, 1, 0) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => _ = HandleLoopCompletedAsync()));
    }

    private void Engine_PlaybackFaulted(object? sender, StoppedEventArgs eventArgs)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            SetStatus($"音频设备停止播放：{eventArgs.Exception?.Message}");
            MessageBox.Show(
                this,
                $"Windows 音频设备停止播放。\n\n{eventArgs.Exception?.Message}",
                "音频设备错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            UpdateUi();
        }));
    }

    private async Task HandlePlaybackEndedAsync()
    {
        try
        {
            await FinishRecordingAsync();
            UpdateUi();
        }
        catch (Exception exception)
        {
            ShowError("结束录音时发生错误", exception);
        }
    }

    private async Task HandleLoopCompletedAsync()
    {
        try
        {
            await FinishRecordingAsync();
            SetStatus("循环完成：录音已保存为一条新音轨，播放将继续。");
        }
        catch (Exception exception)
        {
            ShowError("保存循环录音时发生错误", exception);
        }
        finally
        {
            Interlocked.Exchange(ref _loopRecordingFinishQueued, 0);
        }
    }

    private async Task ImportFilesAsync(IEnumerable<string> filePaths)
    {
        if (_engine is null)
        {
            return;
        }

        var errors = new List<string>();
        var imported = 0;

        foreach (var filePath in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await AddTrackAsync(
                    filePath,
                    Path.GetFileNameWithoutExtension(filePath),
                    TimeSpan.Zero,
                    TrackKind.Imported);
                imported++;
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(filePath)}：{exception.Message}");
            }
        }

        RefreshTrackDisplay();
        ScheduleSessionSave();

        if (errors.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, errors),
                "部分音频无法导入",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        if (imported > 0)
        {
            SetStatus($"已导入 {imported} 条音轨。所有音轨将从同一时间轴同步播放。");
        }
    }

    private async Task<AudioTrackModel> AddTrackAsync(
        string filePath,
        string name,
        TimeSpan offset,
        TrackKind kind,
        double volume = 0.85,
        bool isMuted = false)
    {
        if (_engine is null)
        {
            throw new InvalidOperationException("The audio engine is not initialized.");
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到音频文件。", fullPath);
        }

        TimeSpan duration;
        using (var reader = new AudioFileReader(fullPath))
        {
            duration = reader.TotalTime;
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new FormatException("音频长度为零。");
        }

        var track = new AudioTrackModel(
            fullPath,
            name,
            duration,
            offset,
            kind,
            volume,
            isMuted);

        _engine.AddTrack(track);
        _tracks.Add(track);
        track.PropertyChanged += Track_PropertyChanged;
        RefreshTrackDisplay();

        try
        {
            var peaks = await WaveformExtractor.ExtractAsync(
                fullPath,
                cancellationToken: _lifetimeCancellation.Token);
            track.SetWaveform(peaks);
            Timeline.InvalidateVisual();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetStatus($"“{track.Name}”可播放，但波形生成失败：{exception.Message}");
        }

        return track;
    }

    private bool StartRecording()
    {
        if (_engine is null)
        {
            return false;
        }

        if (RecordingInputCombo.SelectedItem is not RecordingInput input)
        {
            MessageBox.Show(
                this,
                "未检测到可用的录音输入。请连接吉他声卡或麦克风后重新打开应用。",
                "无法开始录音",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var recordingDirectory = Path.Combine(documents, "GuitarPractice", "Recordings");
        var outputPath = Path.Combine(
            recordingDirectory,
            $"Take {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav");

        _recordingStart = _engine.Position;
        _recorder.Start(input.Id, outputPath);
        PositionSlider.IsEnabled = false;
        Timeline.IsHitTestVisible = false;
        RecordingHint.Text = "正在录音。暂停、停止或完成一次循环后会自动生成新音轨。";
        SetStatus($"正在录制吉他输入：{input.Name}");
        return true;
    }

    private Task FinishRecordingAsync()
    {
        if (_finishRecordingTask is { IsCompleted: false })
        {
            return _finishRecordingTask;
        }

        if (!_recorder.IsRecording)
        {
            return Task.CompletedTask;
        }

        _finishRecordingTask = FinishRecordingCoreAsync();
        return _finishRecordingTask;
    }

    private async Task FinishRecordingCoreAsync()
    {
        try
        {
            var path = await _recorder.StopAsync();
            if (path is null || !File.Exists(path))
            {
                return;
            }

            var takeName = $"Take {DateTime.Now:HH:mm:ss}";
            await AddTrackAsync(path, takeName, _recordingStart, TrackKind.Recording, volume: 1);
            RecordArmToggle.IsChecked = false;
            RefreshTrackDisplay();
            ScheduleSessionSave();
            SetStatus($"录音已保存并加入音轨：{path}");
        }
        finally
        {
            PositionSlider.IsEnabled = true;
            Timeline.IsHitTestVisible = true;
        }
    }

    private void LoadRecordingInputs()
    {
        try
        {
            var inputs = AudioRecorder.GetInputs();
            RecordingInputCombo.ItemsSource = inputs;
            RecordingInputCombo.SelectedIndex = inputs.Count > 0 ? 0 : -1;
            RecordArmToggle.IsEnabled = inputs.Count > 0;

            if (inputs.Count == 0)
            {
                RecordingHint.Text = "未检测到录音输入；播放、循环和节拍器仍可正常使用。";
            }
        }
        catch (Exception exception)
        {
            RecordArmToggle.IsEnabled = false;
            RecordingHint.Text = $"无法读取录音输入：{exception.Message}";
        }
    }

    private async Task RestoreSessionAsync()
    {
        if (_engine is null)
        {
            return;
        }

        var session = await _sessionStore.LoadAsync(_lifetimeCancellation.Token);
        if (session is null)
        {
            return;
        }

        _isUpdatingUi = true;
        BpmSlider.Value = Math.Clamp(session.BeatsPerMinute, 40, 240);
        MetronomeToggle.IsChecked = session.MetronomeEnabled;
        MetronomeVolumeSlider.Value = Math.Clamp(session.MetronomeVolume, 0, 1);
        MasterVolumeSlider.Value = Math.Clamp(session.MasterVolume, 0, 1);
        _isUpdatingUi = false;
        ApplyControlsToEngine();

        if (!string.IsNullOrWhiteSpace(session.RecordingInputId))
        {
            var savedInput = RecordingInputCombo.Items
                .OfType<RecordingInput>()
                .FirstOrDefault(input => input.Id == session.RecordingInputId);
            if (savedInput is not null)
            {
                RecordingInputCombo.SelectedItem = savedInput;
            }
        }

        var missingFiles = new List<string>();
        foreach (var entry in session.Tracks)
        {
            if (!File.Exists(entry.FilePath))
            {
                missingFiles.Add(entry.FilePath);
                continue;
            }

            try
            {
                await AddTrackAsync(
                    entry.FilePath,
                    entry.Name,
                    TimeSpan.FromSeconds(Math.Max(0, entry.OffsetSeconds)),
                    entry.Kind,
                    entry.Volume,
                    entry.IsMuted);
            }
            catch (Exception exception)
            {
                missingFiles.Add($"{entry.FilePath} ({exception.Message})");
            }
        }

        if (session.LoopEndSeconds - session.LoopStartSeconds >= 0.05 &&
            session.LoopEndSeconds <= _engine.Duration.TotalSeconds)
        {
            _engine.SetLoopRange(
                TimeSpan.FromSeconds(session.LoopStartSeconds),
                TimeSpan.FromSeconds(session.LoopEndSeconds));
            _engine.SetLoopEnabled(session.LoopEnabled);

            _isUpdatingUi = true;
            LoopToggle.IsChecked = session.LoopEnabled;
            _isUpdatingUi = false;
        }

        if (missingFiles.Count > 0)
        {
            MessageBox.Show(
                this,
                "以下音轨已被移动、删除或无法读取，因此没有恢复：\n\n" +
                string.Join(Environment.NewLine, missingFiles),
                "部分音轨未恢复",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyControlsToEngine()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.BeatsPerMinute = Math.Round(BpmSlider.Value);
        _engine.MetronomeEnabled = MetronomeToggle.IsChecked == true;
        _engine.MetronomeVolume = (float)MetronomeVolumeSlider.Value;
        _engine.MasterVolume = (float)MasterVolumeSlider.Value;
    }

    private void SetLoopStartAtCurrentPosition()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.SetLoopStart(_engine.Position);
        UpdateUi();
        ScheduleSessionSave();
        SetStatus($"循环起点：{TimeDisplay.Format(_engine.LoopStart)}");
    }

    private void SetLoopEndAtCurrentPosition()
    {
        if (_engine is null)
        {
            return;
        }

        _engine.SetLoopEnd(_engine.Position);
        UpdateUi();
        ScheduleSessionSave();
        SetStatus($"循环终点：{TimeDisplay.Format(_engine.LoopEnd)}");
    }

    private void UpdateUi()
    {
        if (_engine is null)
        {
            return;
        }

        var position = _engine.Position;
        var duration = _engine.Duration;

        _isUpdatingUi = true;
        if (!_isDraggingPosition)
        {
            PositionSlider.Maximum = Math.Max(0.001, duration.TotalSeconds);
            PositionSlider.Value = Math.Clamp(position.TotalSeconds, 0, PositionSlider.Maximum);
        }

        PositionLabel.Text = $"{TimeDisplay.Format(position)} / {TimeDisplay.Format(duration)}";
        PlayButton.Content = _engine.IsPlaying ? "❚❚ 暂停" : "▶ 播放";
        LoopToggle.IsChecked = _engine.IsLoopEnabled;
        LoopRangeLabel.Text =
            $"{TimeDisplay.Format(_engine.LoopStart)} — {TimeDisplay.Format(_engine.LoopEnd)}";
        _isUpdatingUi = false;

        Timeline.DurationSeconds = Math.Max(0.001, duration.TotalSeconds);
        Timeline.PositionSeconds = position.TotalSeconds;
        Timeline.IsLoopEnabled = _engine.IsLoopEnabled;
        Timeline.LoopStartSeconds = _engine.LoopStart.TotalSeconds;
        Timeline.LoopEndSeconds = _engine.LoopEnd.TotalSeconds;
        Timeline.InvalidateVisual();
    }

    private void RefreshTrackDisplay()
    {
        TrackCountLabel.Text = $"{_tracks.Count} 条";
        Timeline.Tracks = _tracks;
        Timeline.InvalidateVisual();
        UpdateUi();
    }

    private void Track_PropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        Timeline.InvalidateVisual();
        ScheduleSessionSave();
    }

    private void ScheduleSessionSave()
    {
        if (!_sessionReady)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private PracticeSession CreateSessionSnapshot()
    {
        return new PracticeSession
        {
            Tracks = _tracks.Select(track => new TrackSessionEntry
            {
                FilePath = track.FilePath,
                Name = track.Name,
                Kind = track.Kind,
                OffsetSeconds = track.Offset.TotalSeconds,
                Volume = track.Volume,
                IsMuted = track.IsMuted
            }).ToList(),
            BeatsPerMinute = BpmSlider.Value,
            MetronomeEnabled = MetronomeToggle.IsChecked == true,
            MetronomeVolume = MetronomeVolumeSlider.Value,
            MasterVolume = MasterVolumeSlider.Value,
            LoopEnabled = _engine?.IsLoopEnabled == true,
            LoopStartSeconds = _engine?.LoopStart.TotalSeconds ?? 0,
            LoopEndSeconds = _engine?.LoopEnd.TotalSeconds ?? 0,
            RecordingInputId = (RecordingInputCombo.SelectedItem as RecordingInput)?.Id
        };
    }

    private Task SaveSessionAsync()
    {
        return _sessionStore.SaveAsync(CreateSessionSnapshot());
    }

    private static IReadOnlyList<string> GetSupportedDroppedFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return [];
        }

        return paths
            .Where(File.Exists)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .ToList();
    }

    private void SetStatus(string message)
    {
        StatusLabel.Text = message;
    }

    private void ShowError(string title, Exception exception)
    {
        SetStatus($"{title}：{exception.Message}");
        MessageBox.Show(
            this,
            $"{title}\n\n{exception.Message}",
            "Guitar Practice",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs eventArgs)
    {
        if (_closeAfterSave)
        {
            DisposeResources();
            return;
        }

        eventArgs.Cancel = true;
        MainContent.IsEnabled = false;
        _uiTimer.Stop();
        _saveTimer.Stop();
        SetStatus("正在保存会话…");

        try
        {
            await FinishRecordingAsync();
            if (_sessionReady)
            {
                await SaveSessionAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"关闭前保存失败：{exception.Message}\n\n录音文件（如有）仍保存在“文档\\GuitarPractice\\Recordings”。",
                "保存失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _closeAfterSave = true;
            Close();
        }
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        if (_engine is not null)
        {
            _engine.PlaybackEnded -= Engine_PlaybackEnded;
            _engine.LoopCompleted -= Engine_LoopCompleted;
            _engine.PlaybackFaulted -= Engine_PlaybackFaulted;
        }

        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _recorder.Dispose();
        _engine?.Dispose();
    }
}
