using System.Diagnostics;
using System.Text;

namespace WizardOS.Core.Engine;

/// <summary>
/// Межпроцессный замок корня прогонов: файл <c>.lock</c>, открытый с FileShare.None, внутри PID и время.
/// Замок держится пока жив владелец; ОС снимает его при смерти процесса. Если файл остался от
/// завершившегося процесса (открыть эксклюзивно удалось) — замок считается устаревшим и перехватывается.
/// </summary>
public sealed class RunLock : IDisposable
{
    public const string FileName = ".lock";
    private FileStream? _fs;
    private readonly string _path;

    private RunLock(FileStream fs, string path) { _fs = fs; _path = path; }

    /// <summary>Пытается взять замок. При занятом — false и описание владельца (если удалось прочитать).</summary>
    public static bool TryAcquire(string rootDir, out RunLock? lk, out string? holder)
    {
        Directory.CreateDirectory(rootDir);
        var path = Path.Combine(rootDir, FileName);
        lk = null; holder = null;
        FileStream fs;
        try
        {
            fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            holder = ReadHolder(path) ?? "неизвестный процесс";
            return false;
        }
        // Эксклюзивно открыть удалось: прежний владелец (если был) мёртв — перезаписываем.
        fs.SetLength(0);
        var text = $"pid={Environment.ProcessId} time={DateTimeOffset.Now:O}\n";
        var bytes = new UTF8Encoding(false).GetBytes(text);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(true);
        lk = new RunLock(fs, path);
        return true;
    }

    private static string? ReadHolder(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var t = sr.ReadToEnd().Trim();
            return t.Length == 0 ? null : t;
        }
        catch { return null; } // на Windows эксклюзивно занятый файл не читается
    }

    public void Dispose()
    {
        var fs = Interlocked.Exchange(ref _fs, null);
        if (fs is null) return;
        fs.Dispose();
        try { File.Delete(_path); } catch { }
    }
}
