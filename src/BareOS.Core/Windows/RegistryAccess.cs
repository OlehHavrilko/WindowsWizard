using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BareOS.Core.Windows;

/// <summary>Реализация IRegistryAccess поверх Microsoft.Win32.Registry (64-битное представление).
/// Путь: "HKLM\SOFTWARE\..." (также HKCU, HKCR, HKU, HKCC). На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class RegistryAccess : IRegistryAccess, IDisposable
{
    private const string DefaultUserKey = "BareOS_DefaultUser";
    private static bool _defaultLoaded;

    /// <summary>HKDU — профиль по умолчанию (C:\Users\Default\NTUSER.DAT): куст временно монтируется в HKU\BareOS_DefaultUser (reg load), чтобы новые пользователи получили настройки.</summary>
    [SupportedOSPlatform("windows")]
    private static void EnsureDefaultUserLoaded()
    {
        if (_defaultLoaded) return;
        using (var probe = Registry.Users.OpenSubKey(DefaultUserKey)) { if (probe is not null) { _defaultLoaded = true; return; } }
        var dat = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", "Users", "Default", "NTUSER.DAT");
        RunReg("load", "HKU\\" + DefaultUserKey, dat);
        _defaultLoaded = true;
    }

    private static void RunReg(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("reg.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить reg.exe");
        var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"reg.exe {string.Join(' ', args)}: код {p.ExitCode}: {o.Trim()}");
    }

    /// <summary>Размонтирует куст профиля по умолчанию, если он был смонтирован этим процессом.</summary>
    public void Dispose()
    {
        if (!OperatingSystem.IsWindows() || !_defaultLoaded) return;
        GC.Collect(); GC.WaitForPendingFinalizers();
        try { RunReg("unload", "HKU\\" + DefaultUserKey); _defaultLoaded = false; } catch (InvalidOperationException) { /* куст отпустится при выходе процесса */ }
    }

    public RegValue Read(string path, string name)
    {
        if (!OperatingSystem.IsWindows()) throw NotWindows();
        return ReadCore(path, name);
    }

    public void Write(string path, string name, RegValue value)
    {
        if (!OperatingSystem.IsWindows()) throw NotWindows();
        WriteCore(path, name, value);
    }

    public bool KeyExists(string path)
    {
        if (!OperatingSystem.IsWindows()) throw NotWindows();
        return KeyExistsCore(path);
    }

    public void DeleteKeyIfEmpty(string path)
    {
        if (!OperatingSystem.IsWindows()) throw NotWindows();
        DeleteKeyIfEmptyCore(path);
    }

    [SupportedOSPlatform("windows")]
    private static bool KeyExistsCore(string path)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.OpenSubKey(sub, writable: false))
            return key is not null;
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteKeyIfEmptyCore(string path)
    {
        var (root, sub) = Split(path);
        using (root)
        {
            if (sub.IndexOf('\\') < 0 && sub.Length == 0) return;   // корень куста не трогаем
            using (var key = root.OpenSubKey(sub, writable: false))
            {
                if (key is null || key.ValueCount != 0 || key.SubKeyCount != 0) return;
            }
            // Удаляем от корня куста, а не через родителя, открытого на запись: родитель вроде Policies\Microsoft
            // может не давать права на запись (на 26300 так для Dsh), хотя создание ключа от корня проходит.
            root.DeleteSubKey(sub, throwOnMissingSubKey: false);
        }
    }

    private static PlatformNotSupportedException NotWindows() => new PlatformNotSupportedException("RegistryAccess работает только в Windows.");

    [SupportedOSPlatform("windows")]
    private static (RegistryKey Root, string Sub) Split(string path)
    {
        var i = path.IndexOf('\\');
        var hive = (i < 0 ? path : path[..i]).ToUpperInvariant();
        var sub = i < 0 ? "" : path[(i + 1)..];
        var view = RegistryView.Registry64;
        if (hive == "HKDU")
        {
            EnsureDefaultUserLoaded();
            return (RegistryKey.OpenBaseKey(RegistryHive.Users, view), sub.Length == 0 ? DefaultUserKey : DefaultUserKey + "\\" + sub);
        }
        RegistryHive h = hive switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
            _ => throw new ArgumentException($"Неизвестный куст реестра в пути: {path}")
        };
        return (RegistryKey.OpenBaseKey(h, view), sub);
    }

    [SupportedOSPlatform("windows")]
    private static RegValue ReadCore(string path, string name)
    {
        var (root, sub) = Split(path);
        using (root)
        using (var key = root.OpenSubKey(sub, writable: false))
        {
            if (key is null) return RegValue.Missing;
            var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is null) return RegValue.Missing;
            var kind = key.GetValueKind(name);
            return kind switch
            {
                RegistryValueKind.DWord => new RegValue(true, RegKind.DWord, unchecked((uint)(int)raw).ToString(CultureInfo.InvariantCulture)),
                RegistryValueKind.QWord => new RegValue(true, RegKind.QWord, unchecked((ulong)(long)raw).ToString(CultureInfo.InvariantCulture)),
                RegistryValueKind.String => new RegValue(true, RegKind.String, (string)raw),
                RegistryValueKind.ExpandString => new RegValue(true, RegKind.ExpandString, (string)raw),
                RegistryValueKind.MultiString => new RegValue(true, RegKind.MultiString, string.Join('\n', (string[])raw)),
                RegistryValueKind.Binary => new RegValue(true, RegKind.Binary, Convert.ToHexString((byte[])raw)),
                _ => throw new NotSupportedException($"Тип значения реестра {kind} не поддерживается: {path}\\{name}")
            };
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteCore(string path, string name, RegValue value)
    {
        var (root, sub) = Split(path);
        using (root)
        {
            if (!value.Exists)
            {
                using var k = root.OpenSubKey(sub, writable: true);
                k?.DeleteValue(name, throwOnMissingValue: false);
                return;
            }
            using var key = root.CreateSubKey(sub, writable: true)
                ?? throw new InvalidOperationException($"Не удалось открыть ключ {path}");
            var data = value.Data ?? "";
            switch (value.Kind)
            {
                case RegKind.DWord:
                    key.SetValue(name, unchecked((int)uint.Parse(data, CultureInfo.InvariantCulture)), RegistryValueKind.DWord);
                    break;
                case RegKind.QWord:
                    key.SetValue(name, unchecked((long)ulong.Parse(data, CultureInfo.InvariantCulture)), RegistryValueKind.QWord);
                    break;
                case RegKind.String:
                    key.SetValue(name, data, RegistryValueKind.String);
                    break;
                case RegKind.ExpandString:
                    key.SetValue(name, data, RegistryValueKind.ExpandString);
                    break;
                case RegKind.MultiString:
                    key.SetValue(name, data.Length == 0 ? Array.Empty<string>() : data.Split('\n'), RegistryValueKind.MultiString);
                    break;
                case RegKind.Binary:
                    key.SetValue(name, Convert.FromHexString(data), RegistryValueKind.Binary);
                    break;
                default:
                    throw new NotSupportedException($"Тип {value.Kind}");
            }
        }
    }
}
