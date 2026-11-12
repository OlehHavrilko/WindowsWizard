using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BareOS.Core.Proof;
using Microsoft.Win32;

namespace BareOS.Core.Windows;

/// <summary>Замер текущего состояния системы для Proof Report. Только Windows.</summary>
public static class SystemProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemStatus
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemStatus>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemStatus buffer);

    public static Snapshot Take(string label)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Замер работает только в Windows.");
        return TakeCore(label);
    }

    [SupportedOSPlatform("windows")]
    private static Snapshot TakeCore(string label)
    {
        var m = new Dictionary<string, double>();
        var procs = Process.GetProcesses();
        m["proc.count"] = procs.Length;
        double ws = 0;
        foreach (var p in procs) { try { ws += p.WorkingSet64; } catch { } p.Dispose(); }
        m["proc.workingSetMB"] = Math.Round(ws / 1048576.0);

        var ms = new MemStatus();
        if (GlobalMemoryStatusEx(ms)) m["mem.usedMB"] = Math.Round((ms.ullTotalPhys - ms.ullAvailPhys) / 1048576.0);

        try
        {
            var o = Ps("(Get-AppxPackage -AllUsers | Measure-Object).Count; (Get-AppxProvisionedPackage -Online | Measure-Object).Count");
            var l = o.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (l.Length >= 2 && double.TryParse(l[0], out var a) && double.TryParse(l[1], out var b)) { m["appx.installed"] = a; m["appx.provisioned"] = b; }
        }
        catch { /* метрика пропускается, отчёт сравнит только имеющиеся */ }

        try
        {
            using var hk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            int auto = 0;
            if (hk is not null)
                foreach (var n in hk.GetSubKeyNames())
                {
                    using var k = hk.OpenSubKey(n);
                    if (k?.GetValue("Start") is int s && s == 2 && k.GetValue("Type") is int t && (t & 0x30) != 0) auto++;
                }
            m["svc.autostart"] = auto;
        }
        catch { }

        int run = 0;
        foreach (var (root, path) in new[] { (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run") })
        {
            try { using var k = root.OpenSubKey(path); run += k?.GetValueNames().Length ?? 0; } catch { }
        }
        m["startup.entries"] = run;

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            long size = 0;
            foreach (var sub in Directory.EnumerateDirectories(dir))
                foreach (var f in Directory.EnumerateFiles(sub, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                    try { size += new FileInfo(f).Length; } catch { }
            if (size > 0) m["disk.windowsAppsMB"] = Math.Round(size / 1048576.0);
        }
        catch { }

        return new Snapshot(label, DateTimeOffset.Now, m);
    }

    private static string Ps(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-NonInteractive"); psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add(script);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powershell.exe не запущен");
        var o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
        return o;
    }
}
