using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace WizardOS.Core.Windows;

/// <summary>Реализация IRegistryAccess поверх Microsoft.Win32.Registry (64-битное представление).
/// Путь: "HKLM\SOFTWARE\..." (также HKCU, HKCR, HKU, HKCC). На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class RegistryAccess : IRegistryAccess
{
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

    private static PlatformNotSupportedException NotWindows() => new PlatformNotSupportedException("RegistryAccess работает только в Windows.");

    [SupportedOSPlatform("windows")]
    private static (RegistryKey Root, string Sub) Split(string path)
    {
        var i = path.IndexOf('\\');
        var hive = (i < 0 ? path : path[..i]).ToUpperInvariant();
        var sub = i < 0 ? "" : path[(i + 1)..];
        var view = RegistryView.Registry64;
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
