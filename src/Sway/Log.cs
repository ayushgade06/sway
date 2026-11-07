using System.IO;
namespace Sway;

/// <summary>Local, size-capped log at %LOCALAPPDATA%\Sway\log.txt. Never contains touch coordinates (D-16).</summary>
static class Log
{
    const long MaxBytes = 512 * 1024;
    static readonly object Lock = new();

    public static string Dir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sway");
    public static string FilePath { get; } = Path.Combine(Dir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(Dir, "log.old.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
