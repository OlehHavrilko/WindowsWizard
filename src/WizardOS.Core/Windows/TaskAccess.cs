using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WizardOS.Core.Windows;

/// <summary>Реализация ITaskAccess через schtasks.exe. Состояние читается из XML задачи
/// (&lt;Settings&gt;&lt;Enabled&gt;; отсутствие элемента = включена), поэтому не зависит от языка системы.
/// Код возврата ≠ 0 при чтении трактуется как «задачи нет», если файла задачи в System32\Tasks тоже нет (иначе ошибка чтения). На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class TaskAccess : ITaskAccess
{
    public TaskState Read(string path)
    {
        if (!OperatingSystem.IsWindows()) throw NotWindows();
        var (code, output) = Run("/Query", "/TN", path, "/XML");
        if (code != 0)
        {
            // schtasks возвращает 1 и на «не найдено», и на «отказано в доступе». Различаем по файлу задачи:
            // файл есть — это ошибка чтения, а не отсутствие (иначе apply счёл бы Disabled уже применённым, а baseline — пустым).
            if (TaskFileExists(path))
                throw new InvalidOperationException($"schtasks /Query {path} завершился с кодом {code}, хотя задача зарегистрирована: {output.Trim()}");
            return new TaskState(false);
        }
        return ParseXml(output);
    }

    public void SetEnabled(string path, bool enabled)
    {
        if (!OperatingSystem.IsWindows()) throw NotWindows();
        var (code, output) = Run("/Change", "/TN", path, enabled ? "/ENABLE" : "/DISABLE");
        if (code != 0)
            throw new InvalidOperationException($"schtasks /Change {path} {(enabled ? "/ENABLE" : "/DISABLE")} завершился с кодом {code}: {output.Trim()}");
    }

    private static readonly TimeSpan RxTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Разбор XML задачи (публично для тестов): включена ли задача.</summary>
    public static TaskState ParseXml(string xml)
    {
        xml = xml.Replace("\0", "");   // на случай вывода в UTF-16
        if (!Regex.IsMatch(xml, @"<Task\b", RegexOptions.IgnoreCase, RxTimeout))
            throw new InvalidOperationException("schtasks /XML вернул не XML задачи: " + (xml.Length > 200 ? xml[..200] : xml).Trim());
        var settings = Regex.Match(xml, @"<Settings\b.*?</Settings>", RegexOptions.Singleline | RegexOptions.IgnoreCase, RxTimeout);
        if (settings.Success)
        {
            var en = Regex.Match(settings.Value, @"<Enabled>\s*(true|false)\s*</Enabled>", RegexOptions.IgnoreCase, RxTimeout);
            if (en.Success) return new TaskState(true, en.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }
        return new TaskState(true, true);
    }

    private static bool TaskFileExists(string path)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
        try { File.GetAttributes(root + path.Replace('/', '\\')); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        // UnauthorizedAccessException и прочее: файл есть/недоступен — не считаем «задачи нет»
    }

    private static PlatformNotSupportedException NotWindows() => new PlatformNotSupportedException("TaskAccess работает только в Windows.");

    private static (int Code, string Output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить schtasks.exe");
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + err.Result);
    }
}
