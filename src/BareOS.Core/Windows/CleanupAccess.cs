namespace BareOS.Core.Windows;

/// <summary>Реализация ICleanupAccess (D-035). ComponentStore — dism /Online /Cleanup-Image /StartComponentCleanup
/// (без /ResetBase, код 3010 не ошибка). Temp — содержимое %WINDIR%\Temp и %TEMP%; занятые и недоступные файлы
/// пропускаются молча, точки повторной обработки (junction/symlink) не разворачиваются. Только Windows.</summary>
public sealed class CleanupAccess : ICleanupAccess
{
    public long FreeBytes()
    {
        EnsureWindows();
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? throw new InvalidOperationException("Не определён системный диск");
        return new DriveInfo(root).AvailableFreeSpace;
    }

    public void ComponentStore()
    {
        EnsureWindows();
        var (code, output) = DismRunner.Run("/Online", "/Cleanup-Image", "/StartComponentCleanup");
        if (code != 0 && code != DismRunner.RebootRequired)
        {
            var t = output.Trim();
            throw new InvalidOperationException($"DISM StartComponentCleanup завершился с кодом {code}: {(t.Length <= 400 ? t : t[^400..])}");
        }
    }

    public void Temp()
    {
        EnsureWindows();
        var dirs = new List<string>();
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (win != "") dirs.Add(Path.Combine(win, "Temp"));
        var tmp = Path.GetTempPath().TrimEnd('\\', '/');
        if (tmp != "") dirs.Add(tmp);
        foreach (var d in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            if (IsSafeRoot(d)) Clear(new DirectoryInfo(d));
    }

    /// <summary>Не корень диска и не каталог короче двух уровней: защита от неверной переменной среды.</summary>
    public static bool IsSafeRoot(string dir)
    {
        var full = Path.GetFullPath(dir);
        var root = Path.GetPathRoot(full) ?? "";
        return full.TrimEnd('\\', '/').Length > root.TrimEnd('\\', '/').Length
            && full.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2
            && Directory.Exists(full);
    }

    private static void Clear(DirectoryInfo dir)
    {
        IEnumerable<FileSystemInfo> items;
        try { items = dir.EnumerateFileSystemInfos().ToList(); } catch (Exception) { return; }
        foreach (var item in items)
        {
            try
            {
                if (item is DirectoryInfo sub)
                {
                    if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) { sub.Delete(false); continue; }   // только сама ссылка
                    Clear(sub);
                    sub.Delete(false);   // не пустой (занятые файлы) — IOException, пропускаем
                }
                else
                {
                    if (item.Attributes.HasFlag(FileAttributes.ReadOnly)) item.Attributes = FileAttributes.Normal;
                    item.Delete();
                }
            }
            catch (Exception) { /* занято процессом или нет доступа: пропускаем */ }
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("CleanupAccess работает только в Windows.");
    }
}
