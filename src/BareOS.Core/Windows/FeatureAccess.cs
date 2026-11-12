using System.Text.RegularExpressions;

namespace BareOS.Core.Windows;

/// <summary>Реализация IFeatureAccess через dism.exe (D-035): /Get-FeatureInfo /English, /Disable-Feature /NoRestart,
/// /Enable-Feature /All /NoRestart. Код 3010 (нужна перезагрузка) — не ошибка. На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class FeatureAccess : IFeatureAccess
{
    private static readonly Regex NameRx = new(@"^[A-Za-z0-9._-]{3,128}$", RegexOptions.Compiled);
    private static readonly Regex StateRx = new(@"^\s*State\s*:\s*(.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public FeatureState Read(string name)
    {
        EnsureWindows();
        var (code, output) = DismRunner.Run("/Online", "/Get-FeatureInfo", "/FeatureName:" + Validate(name), "/English");
        return Parse(code, output, name);
    }

    public void SetEnabled(string name, bool enabled)
    {
        EnsureWindows();
        var n = Validate(name);
        var (code, output) = enabled
            ? DismRunner.Run("/Online", "/Enable-Feature", "/FeatureName:" + n, "/All", "/NoRestart")
            : DismRunner.Run("/Online", "/Disable-Feature", "/FeatureName:" + n, "/NoRestart");
        if (code != 0 && code != DismRunner.RebootRequired)
            throw new InvalidOperationException($"DISM {(enabled ? "Enable" : "Disable")}-Feature '{name}' завершился с кодом {code}: {Tail(output)}");
    }

    /// <summary>Имя компонента: только безопасные символы (никакой инъекции в аргументы).</summary>
    public static string Validate(string name)
    {
        if (!NameRx.IsMatch(name)) throw new ArgumentException($"Недопустимое имя компонента: '{name}'");
        return name;
    }

    /// <summary>Разбор вывода /Get-FeatureInfo (публично для тестов). Enabled и Enable Pending — включён,
    /// Disabled, Disable Pending и «Disabled with Payload Removed» — выключен; «компонент неизвестен»
    /// (0x800f080c) — Exists=false; прочие ошибки бросают исключение.</summary>
    public static FeatureState Parse(int exitCode, string output, string name = "")
    {
        if (exitCode != 0)
        {
            if (output.Contains("0x800f080c", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Feature name", StringComparison.OrdinalIgnoreCase) && output.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                return new FeatureState(false);
            throw new InvalidOperationException($"DISM Get-FeatureInfo '{name}' завершился с кодом {exitCode}: {Tail(output)}");
        }
        var m = StateRx.Match(output);
        if (!m.Success) throw new InvalidOperationException($"DISM Get-FeatureInfo '{name}': в выводе нет строки State");
        var st = m.Groups[1].Value.Trim().ToLowerInvariant();
        if (st.StartsWith("enable", StringComparison.Ordinal)) return new FeatureState(true, true);   // Enabled, Enable Pending
        if (st.StartsWith("disable", StringComparison.Ordinal)) return new FeatureState(true, false); // Disabled, Disable Pending, Disabled with Payload Removed
        throw new InvalidOperationException($"DISM Get-FeatureInfo '{name}': неизвестное состояние '{m.Groups[1].Value}'");
    }

    private static string Tail(string s) { s = s.Trim(); return s.Length <= 400 ? s : s[^400..]; }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("FeatureAccess работает только в Windows.");
    }
}
