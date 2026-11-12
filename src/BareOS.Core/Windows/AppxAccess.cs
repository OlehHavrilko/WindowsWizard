using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BareOS.Core.Windows;

/// <summary>Реализация IAppxAccess через Windows PowerShell 5.1 (модуль Appx не работает в PowerShell 7).
/// Поиск: Get-AppxPackage -AllUsers и Get-AppxProvisionedPackage -Online; удаление: Remove-AppxPackage -AllUsers
/// и Remove-AppxProvisionedPackage -Online. На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class AppxAccess : IAppxAccess
{
    private static readonly Regex PatternRx = new(@"^[A-Za-z0-9._*-]{3,128}$", RegexOptions.Compiled);

    public IReadOnlyList<string> Find(string namePattern)
    {
        EnsureWindows();
        var p = Quote(namePattern);
        var script = $"Get-AppxPackage -AllUsers -Name {p} | ForEach-Object {{ 'installed:' + $_.PackageFullName }}; "
                   + $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like {p} }} | ForEach-Object {{ 'provisioned:' + $_.PackageName }}";
        var (code, output) = RunPowerShell(script);
        if (code != 0) throw new InvalidOperationException($"Поиск Appx '{namePattern}' завершился с кодом {code}: {output.Trim()}");
        return ParseLines(output);
    }

    public void Remove(string namePattern)
    {
        EnsureWindows();
        var p = Quote(namePattern);
        var script = $"Get-AppxPackage -AllUsers -Name {p} | Remove-AppxPackage -AllUsers; "
                   + $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like {p} }} | Remove-AppxProvisionedPackage -Online | Out-Null";
        var (code, output) = RunPowerShell(script);
        if (code != 0) throw new InvalidOperationException($"Удаление Appx '{namePattern}' завершилось с кодом {code}: {output.Trim()}");
    }

    /// <summary>Разбор вывода поиска (публично для тестов): строки вида installed:… / provisioned:…, без дубликатов.</summary>
    public static IReadOnlyList<string> ParseLines(string output)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(l => l.StartsWith("installed:", StringComparison.Ordinal) || l.StartsWith("provisioned:", StringComparison.Ordinal))
                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Шаблон в одинарных кавычках PowerShell; допустимы только безопасные символы (никакой инъекции).</summary>
    public static string Quote(string pattern)
    {
        if (!PatternRx.IsMatch(pattern)) throw new ArgumentException($"Недопустимый шаблон имени Appx: '{pattern}'");
        return "'" + pattern + "'";
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AppxAccess работает только в Windows.");
    }

    private static (int Code, string Output) RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                                  "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; " + script })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить powershell.exe");
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + err.Result);
    }
}
