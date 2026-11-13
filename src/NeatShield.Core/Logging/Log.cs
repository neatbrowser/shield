namespace NeatShield.Core.Logging;

/// <summary>Very small file logger. Never throws.</summary>
public static class Log
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "neatshield.log");
        }
        catch
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var path = _path;
        if (path is null) return;
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = path + ".1";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(path, old);
                }

                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
