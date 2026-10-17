using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace GuitarPractice.App.Recording;

public sealed class AudioRecorder : IDisposable
{
    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<string>? _completion;
    private string? _outputPath;
    private Exception? _writeException;
    private bool _isStopping;

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _capture is not null;
            }
        }
    }

    public static IReadOnlyList<RecordingInput> GetInputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new RecordingInput(device.ID, device.FriendlyName))
            .ToList();
    }

    public void Start(string deviceId, string outputPath)
    {
        lock (_gate)
        {
            if (_capture is not null)
            {
                throw new InvalidOperationException("A recording is already in progress.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                ?? throw new ArgumentException("The recording path has no directory.", nameof(outputPath)));

            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDevice(deviceId);
            _capture = new WasapiCapture(_device);
            _writer = new WaveFileWriter(outputPath, _capture.WaveFormat);
            _completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _outputPath = outputPath;

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            try
            {
                _capture.StartRecording();
            }
            catch
            {
                CleanupCapture();
                throw;
            }
        }
    }

    public Task<string?> StopAsync()
    {
        WasapiCapture? capture;
        Task<string>? completion;
        var shouldStop = false;

        lock (_gate)
        {
            if (_capture is null || _completion is null)
            {
                return Task.FromResult<string?>(null);
            }

            capture = _capture;
            completion = _completion.Task;
            if (!_isStopping)
            {
                _isStopping = true;
                shouldStop = true;
            }
        }

        if (shouldStop)
        {
            try
            {
                capture.StopRecording();
            }
            catch (Exception exception)
            {
                TaskCompletionSource<string>? failedCompletion;
                lock (_gate)
                {
                    failedCompletion = _completion;
                    CleanupCapture();
                }

                failedCompletion?.TrySetException(exception);
            }
        }

        return AwaitPathAsync(completion);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private static async Task<string?> AwaitPathAsync(Task<string> completion)
    {
        return await completion.ConfigureAwait(false);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        WasapiCapture? captureToStop = null;

        lock (_gate)
        {
            try
            {
                _writer?.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            }
            catch (Exception exception)
            {
                _writeException = exception;
                if (!_isStopping)
                {
                    _isStopping = true;
                    captureToStop = _capture;
                }
            }
        }

        if (captureToStop is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => StopAfterWriteFailure(captureToStop));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        TaskCompletionSource<string>? completion;
        string? outputPath;
        Exception? failure;

        lock (_gate)
        {
            completion = _completion;
            outputPath = _outputPath;
            failure = eventArgs.Exception ?? _writeException;
            CleanupCapture();
        }

        if (failure is not null)
        {
            completion?.TrySetException(failure);
        }
        else if (outputPath is not null)
        {
            completion?.TrySetResult(outputPath);
        }
    }

    private void CleanupCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
        }

        _writer?.Dispose();
        _capture?.Dispose();
        _device?.Dispose();
        _writer = null;
        _capture = null;
        _device = null;
        _completion = null;
        _outputPath = null;
        _writeException = null;
        _isStopping = false;
    }

    private void StopAfterWriteFailure(WasapiCapture capture)
    {
        try
        {
            capture.StopRecording();
        }
        catch (Exception stopException)
        {
            TaskCompletionSource<string>? completion;
            Exception failure;

            lock (_gate)
            {
                completion = _completion;
                failure = _writeException ?? stopException;
                CleanupCapture();
            }

            completion?.TrySetException(failure);
        }
    }
}
