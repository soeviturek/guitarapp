using System.IO;
using System.Text.Json;

namespace GuitarPractice.App.Persistence;

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _sessionPath;

    public SessionStore()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _sessionPath = Path.Combine(applicationData, "GuitarPractice", "session.json");
    }

    public async Task<PracticeSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_sessionPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_sessionPath);
        return await JsonSerializer.DeserializeAsync<PracticeSession>(
            stream,
            SerializerOptions,
            cancellationToken);
    }

    public async Task SaveAsync(
        PracticeSession session,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_sessionPath)
            ?? throw new InvalidOperationException("The session path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _sessionPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                session,
                SerializerOptions,
                cancellationToken);
        }

        File.Move(temporaryPath, _sessionPath, true);
    }
}
