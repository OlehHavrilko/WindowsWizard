using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace WizardOS.Core.Engine;

/// <summary>Файловое хранилище прогонов: root/&lt;baselineId&gt;/baseline.json, root/&lt;runId&gt;/report.json и journal.jsonl.</summary>
public sealed class JsonRunStore : IRunStore
{
    private readonly string _root;
    private readonly object _lock = new();

    public JsonRunStore(string rootDir)
    {
        if (string.IsNullOrWhiteSpace(rootDir)) throw new ArgumentException("rootDir is empty", nameof(rootDir));
        _root = rootDir;
    }

    private string Dir(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or ".." || id.Contains(".."))
            throw new ArgumentException($"Недопустимый идентификатор: '{id}'");
        return Path.Combine(_root, id);
    }

    public void SaveBaseline(Baseline baseline)
    {
        var dir = Dir(baseline.BaselineId);
        var path = Path.Combine(dir, "baseline.json");
        lock (_lock)
        {
            if (File.Exists(path))
                throw new InvalidOperationException($"Baseline '{baseline.BaselineId}' уже существует и не перезаписывается.");
            EnsureRunDir(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(baseline, EngineJson.Indented));
            try
            {
                File.Move(tmp, path, overwrite: false); // не перезаписывает
            }
            catch (IOException) when (File.Exists(path))
            {
                TryDelete(tmp);
                throw new InvalidOperationException($"Baseline '{baseline.BaselineId}' уже существует и не перезаписывается.");
            }
            catch { TryDelete(tmp); throw; }
        }
    }

    public Baseline? LoadLatestBaseline() => LoadBaselines().LastOrDefault();

    private readonly List<string> _corrupt = new();
    /// <summary>Повреждённые baseline.json, пропущенные при чтении; CLI показывает их предупреждением.
    /// Цели, для которых снимка не нашлось, откат отметит Failed (без записи).</summary>
    public IReadOnlyList<string> CorruptFiles => _corrupt;

    /// <summary>Все baseline по возрастанию CreatedAt.</summary>
    public IReadOnlyList<Baseline> LoadBaselines()
    {
        if (!Directory.Exists(_root)) return Array.Empty<Baseline>();
        var list = new List<Baseline>();
        foreach (var d in Directory.EnumerateDirectories(_root))
        {
            var f = Path.Combine(d, "baseline.json");
            if (!File.Exists(f)) continue;
            Baseline? b = null;
            try { b = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(f), EngineJson.Options); }
            catch (JsonException) { }
            if (b?.Entries == null) { _corrupt.Add(f); continue; }   // один битый старый файл не должен блокировать всё остальное
            list.Add(b);
        }
        return list.OrderBy(b => b.CreatedAt).ToList();
    }

    /// <summary>Создаёт корень хранилища и (на Windows) закрывает его ACL: только Администраторы и SYSTEM.
    /// Иначе обычный пользователь может подложить baseline.json в %ProgramData%, и revert от администратора запишет его значения.
    /// Вызывается перед apply/revert (они и так требуют прав администратора).</summary>
    public void SecureRoot()
    {
        var existed = Directory.Exists(_root);
        Directory.CreateDirectory(_root);
        // Чужой каталог (--data на папку с файлами) не трогаем: ACL ставим только на новый или пустой корень.
        if (OperatingSystem.IsWindows() && (!existed || !Directory.EnumerateFileSystemEntries(_root).Any(e => Path.GetFileName(e) != RunLock.FileName)))
            RestrictAcl(_root);
    }

    public void SaveReport(Report report)
    {
        var dir = Dir(report.RunId);
        lock (_lock)
        {
            EnsureRunDir(dir);
            AtomicWrite(Path.Combine(dir, "report.json"), JsonSerializer.Serialize(report, EngineJson.Indented));
        }
    }

    public void SaveJournalLine(string runId, ReportEntry entry)
    {
        var dir = Dir(runId);
        lock (_lock)
        {
            EnsureRunDir(dir);
            var line = JsonSerializer.Serialize(entry, EngineJson.Compact) + "\n";
            using var fs = new FileStream(Path.Combine(dir, "journal.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read);
            var bytes = new System.Text.UTF8Encoding(false).GetBytes(line);
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
    }

    /// <summary>Создаёт каталог прогона; на Windows ограничивает ACL администраторами и SYSTEM.</summary>
    private static void EnsureRunDir(string dir)
    {
        if (Directory.Exists(dir)) return;
        Directory.CreateDirectory(dir);
        if (OperatingSystem.IsWindows()) RestrictAcl(dir);
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictAcl(string dir)
    {
        var info = new DirectoryInfo(dir);
        var sec = new DirectorySecurity();
        sec.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));   // владелец не может вернуть себе доступ через WRITE_DAC
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(sec);
    }

    public Report? LoadReport(string runId)
    {
        var f = Path.Combine(Dir(runId), "report.json");
        return File.Exists(f) ? JsonSerializer.Deserialize<Report>(File.ReadAllText(f), EngineJson.Options) : null;
    }

    public Report? LoadLatestReport()
    {
        if (!Directory.Exists(_root)) return null;
        Report? best = null;
        foreach (var d in Directory.EnumerateDirectories(_root))
        {
            var f = Path.Combine(d, "report.json");
            if (!File.Exists(f)) continue;
            var r = JsonSerializer.Deserialize<Report>(File.ReadAllText(f), EngineJson.Options);
            if (r != null && (best == null || r.At > best.At)) best = r;
        }
        return best;
    }

    private static void AtomicWrite(string path, string content)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, content);
        try
        {
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch { TryDelete(tmp); throw; }
    }

    private static void TryDelete(string p) { try { File.Delete(p); } catch { } }
}
