using System.Text.Json;

namespace Caffeine.Core.Common;

/// <summary>
/// Typed JSON persistence for one document under %LocalAppData%\Caffeine\.
/// Writes are atomic (temp file, then move/replace) so a crash mid-save can
/// never corrupt the store. A corrupt file on load is renamed to
/// *.corrupt.bak and replaced with a fresh document — the app never crashes
/// over bad data. All I/O is best-effort, mirroring the old services.
/// </summary>
public sealed class JsonStore<T>
    where T : class, new()
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public JsonStore(string fileName, string? directory = null)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caffeine");
        _path = Path.Combine(directory, fileName);
    }

    public T Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new T();
            }

            return JsonSerializer.Deserialize<T>(File.ReadAllText(_path), Options) ?? new T();
        }
        catch
        {
            BackupCorruptFile();
            return new T();
        }
    }

    public void Save(T document)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(document, Options));
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Persistence is best-effort; never take the app down over disk I/O.
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            File.Move(_path, _path + ".corrupt.bak", overwrite: true);
        }
        catch
        {
        }
    }
}
