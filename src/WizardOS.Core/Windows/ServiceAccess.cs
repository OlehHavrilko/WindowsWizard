using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WizardOS.Core.Windows;

/// <summary>Реализация IServiceAccess: чтение типа запуска из HKLM\SYSTEM\CurrentControlSet\Services\&lt;имя&gt;\Start,
/// запись через <c>sc.exe config &lt;имя&gt; start= ...</c>. На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class ServiceAccess : IServiceAccess
{
    public ServiceState Read(string name)
    {
        EnsureWindows();
        return ReadCore(name);
    }

    public void SetStartType(string name, StartType start)
    {
        EnsureWindows();
        var arg = start switch
        {
            StartType.Boot => "boot",
            StartType.System => "system",
            StartType.Automatic => "auto",
            StartType.Manual => "demand",
            StartType.Disabled => "disabled",
            _ => throw new ArgumentException($"Нельзя установить тип запуска {start}")
        };
        RunSc(name, arg);
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ServiceAccess работает только в Windows.");
    }

    [SupportedOSPlatform("windows")]
    private static ServiceState ReadCore(string name)
    {
        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name, writable: false);
        if (key is null) return new ServiceState(false);
        var raw = key.GetValue("Start");
        if (raw is not int v) return new ServiceState(true, StartType.Unknown);
        return new ServiceState(true, v switch
        {
            0 => StartType.Boot,
            1 => StartType.System,
            2 => StartType.Automatic,
            3 => StartType.Manual,
            4 => StartType.Disabled,
            _ => StartType.Unknown
        });
    }

    private static void RunSc(string name, string start)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("config");
        psi.ArgumentList.Add(name);
        psi.ArgumentList.Add("start=");
        psi.ArgumentList.Add(start);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить sc.exe");
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"sc.exe config {name} start= {start} завершился с кодом {p.ExitCode}: {output.Trim()}");
    }
}
