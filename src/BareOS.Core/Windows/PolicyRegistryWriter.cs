namespace BareOS.Core.Windows;

/// <summary>Запись значения через локальную групповую политику (Registry.pol + gpupdate), когда UCPD блокирует
/// прямую запись в ключ (D-031). Защиту ОС не снимает: пишет только тем путём, которым пишет сама Windows
/// (gpsvc по команде gpupdate), и который UCPD не фильтрует. На не-Windows бросает PlatformNotSupportedException.</summary>
public sealed class PolicyRegistryWriter : IPolicyWriter
{
    private readonly IRegistryAccess _registry;
    private readonly Action<bool> _runGpUpdate;   // true = machine, false = user

    public PolicyRegistryWriter(IRegistryAccess registry) : this(registry, RunGpUpdate) { }

    /// <summary>Для тестов: подменяет запуск gpupdate.exe.</summary>
    internal PolicyRegistryWriter(IRegistryAccess registry, Action<bool> runGpUpdate)
    {
        _registry = registry;
        _runGpUpdate = runGpUpdate;
    }

    public bool TrySet(string path, string name, RegValue value)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("PolicyRegistryWriter работает только в Windows.");

        var (machine, sub) = SplitHive(path);
        string polPath = PolFilePath(machine);
        Directory.CreateDirectory(Path.GetDirectoryName(polPath)!);
        bool existedBefore = File.Exists(polPath);
        byte[]? originalBytes = existedBefore ? File.ReadAllBytes(polPath) : null;
        var entries = existedBefore ? PRegFile.Parse(originalBytes!).ToList() : new List<PRegFile.Entry>();
        entries.RemoveAll(e => e.Key.Equals(sub, StringComparison.OrdinalIgnoreCase)
            && (e.Value.Equals(name, StringComparison.OrdinalIgnoreCase)
                || e.Value.Equals(DeleteMarker(name), StringComparison.OrdinalIgnoreCase)));
        entries.Add(value.Exists ? ToSetEntry(sub, name, value) : ToDeleteEntry(sub, name));
        File.WriteAllBytes(polPath, PRegFile.Serialize(entries));

        _runGpUpdate(machine);
        bool ok;
        try { ok = Same(_registry.Read(path, name), value); } catch { ok = false; }
        if (!ok)
        {
            // Политика не применилась (сам UCPD или gpsvc отказал на защищённом ключе): не оставляем в Registry.pol
            // запись, которая ничего не делает, но выглядит как применённая политика.
            RevertPolFile(polPath, existedBefore, originalBytes);
            _runGpUpdate(machine);
        }
        return ok;
    }

    private static void RevertPolFile(string polPath, bool existedBefore, byte[]? originalBytes)
    {
        try
        {
            if (existedBefore) File.WriteAllBytes(polPath, originalBytes!);
            else File.Delete(polPath);
        }
        catch { /* best-effort: неудачная запись не должна скрывать исходный отказ BlockedByOS */ }
    }

    private static string DeleteMarker(string name) => "**del." + name;

    private static bool Same(RegValue a, RegValue b)
    {
        if (!a.Exists || !b.Exists) return a.Exists == b.Exists;
        return a.Kind == b.Kind && string.Equals(a.Data ?? "", b.Data ?? "",
            a.Kind == RegKind.Binary ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>"HKLM\SOFTWARE\..." -> (machine: true, "SOFTWARE\..."). HKCU идёт в User\Registry.pol.</summary>
    private static (bool machine, string sub) SplitHive(string path)
    {
        int i = path.IndexOf('\\');
        if (i < 0) throw new ArgumentException("Путь реестра без куста: " + path, nameof(path));
        string hive = path[..i].ToUpperInvariant();
        if (hive != "HKLM" && hive != "HKCU")
            throw new NotSupportedException($"Групповая политика поддерживает только HKLM/HKCU, получено: {hive}");
        return (hive == "HKLM", path[(i + 1)..]);
    }

    private static string PolFilePath(bool machine) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "GroupPolicy", machine ? "Machine" : "User", "Registry.pol");

    private static PRegFile.Entry ToSetEntry(string sub, string name, RegValue v)
    {
        var (type, data) = Encode(v);
        return new PRegFile.Entry(sub, name, type, data);
    }

    private static PRegFile.Entry ToDeleteEntry(string sub, string name)
        => new(sub, DeleteMarker(name), 1u /* REG_SZ */, Wide(""));

    private static (uint type, byte[] data) Encode(RegValue v) => v.Kind switch
    {
        RegKind.DWord => (4u, BitConverter.GetBytes((uint)long.Parse(v.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture))),
        RegKind.QWord => (11u, BitConverter.GetBytes(ulong.Parse(v.Data ?? "0", System.Globalization.CultureInfo.InvariantCulture))),
        RegKind.String => (1u, Wide(v.Data ?? "")),
        RegKind.ExpandString => (2u, Wide(v.Data ?? "")),
        RegKind.MultiString => (7u, WideMulti((v.Data ?? "").Split('\n'))),
        RegKind.Binary => (3u, Convert.FromHexString(v.Data ?? "")),
        _ => throw new NotSupportedException(v.Kind.ToString()),
    };

    private static byte[] Wide(string s) => System.Text.Encoding.Unicode.GetBytes(s).Concat(new byte[] { 0, 0 }).ToArray();

    private static byte[] WideMulti(IEnumerable<string> parts)
    {
        using var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(Wide(p));
        ms.Write(new byte[] { 0, 0 });
        return ms.ToArray();
    }

    private static void RunGpUpdate(bool machine)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "gpupdate.exe", machine ? "/target:computer /force" : "/target:user /force")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        p?.WaitForExit(60_000);
    }
}
