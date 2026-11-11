using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WizardOS.Core.Windows;

/// <summary>Реализация ITaskAccess через schtasks.exe. Состояние читается из XML задачи
/// (&lt;Settings&gt;&lt;Enabled&gt;; отсутствие элемента = включена), поэтому не зависит от языка системы.
/// Код возврата ≠ 0 при чтении трактуется как «задачи нет». На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class TaskAccess : ITaskAccess
{
    public TaskState Read(string path)
    {
        EnsureWindows();
        var (code, output) = Run("/Query", "/TN", path, "/XML");
        if (code != 0) return new TaskState(false);
        return ParseXml(output);
    }

    public void SetEnabled(string path, bool enabled)
    {
        EnsureWindows();
        var (code, output) = Run("/Change", "/TN", path, enabled ? "/ENABLE" : "/DISABLE");
        if (code != 0)
            throw new InvalidOperationException($"schtasks /Change {path} {(enabled ? "/ENABLE" : "/DISABLE")} завершился с кодом {code}: {output.Trim()}");
    }

    /// <summary>Разбор XML задачи (публично для тестов): включена ли задача.</summary>
    public static TaskState ParseXml(string xml)
    {
        var settings = Regex.Match(xml, @"<Settings\b.*?</Settings>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (settings.Success)
        {
            var en = Regex.Match(settings.Value, @"<Enabled>\s*(true|false)\s*</Enabled>", RegexOptions.IgnoreCase);
            if (en.Success) return new TaskState(true, en.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }
        return new TaskState(true, true);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("TaskAccess работает только в Windows.");
    }

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
