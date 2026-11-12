using BareOS.Core;
using BareOS.Core.Engine;
using BareOS.Core.Proof;
using BareOS.Core.Windows;

namespace BareOS.Toolbox;

public enum TweakState { Unknown, Off, On, Partial }

/// <summary>Единственная точка, где Toolbox касается движка. Твики применяются только через TweakEngine
/// (ARCHITECTURE §«Toolbox не применяет твики в обход движка»): включить = apply, выключить = revert.</summary>
public sealed class EngineHost
{
    public string CatalogDir { get; }
    public string DataDir { get; }
    public string ProofDir { get; }
    public IReadOnlyList<Tweak> Tweaks { get; }

    private readonly object _gate = new();

    private EngineHost(string catalogDir, IReadOnlyList<Tweak> tweaks)
    {
        CatalogDir = catalogDir;
        Tweaks = tweaks;
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        DataDir = Path.Combine(common, "BareOS", "runs");
        ProofDir = Path.Combine(common, "BareOS", "proof");
    }

    public static EngineHost Create()
    {
        var dir = FindCatalog() ?? throw new DirectoryNotFoundException("Каталог параметров (catalog) не найден рядом с программой.");
        return new EngineHost(dir, TweakCatalog.LoadDirectory(dir));
    }

    private static string? FindCatalog()
    {
        var starts = new[] { AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath) ?? "" };
        foreach (var s in starts.Where(s => s.Length > 0).Distinct())
        {
            // рядом с exe (установка: %ProgramFiles%\Bare OS) и вверх по дереву (запуск из репозитория)
            for (var d = new DirectoryInfo(s); d != null; d = d.Parent)
            {
                var c = Path.Combine(d.FullName, "catalog");
                if (Directory.Exists(c) && Directory.EnumerateFiles(c, "*.yml").Any()) return c;
            }
        }
        return null;
    }

    public static bool IsActive(Tweak t) => t.Status is null || t.Status is "accepted" or "verified";

    /// <summary>Appx без отката: вернуть только из Store/winget (D-033).</summary>
    public static bool IsIrreversible(Tweak t) => t.Actions.Count > 0 && t.Actions.All(a => a.Appx != null);

    private TweakEngine NewEngine(IRunStore store)
    {
        var registry = new RegistryAccess();
        var env = new EngineEnv(registry, new ServiceAccess(), () => DateTimeOffset.Now, new TaskAccess(), new PolicyRegistryWriter(registry), new AppxAccess());
        var runId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..4];
        return new TweakEngine(env, store, runId);
    }

    /// <summary>Текущее состояние всех карточек: verify только на чтение, ничего не пишет.</summary>
    public Dictionary<string, TweakState> ReadStates()
    {
        lock (_gate)
        {
            var engine = NewEngine(new NullRunStore(new JsonRunStore(DataDir)));
            var report = engine.Verify(Tweaks);
            return StatesFrom(report);
        }
    }

    public static Dictionary<string, TweakState> StatesFrom(Report report)
    {
        var map = new Dictionary<string, TweakState>();
        foreach (var g in report.Entries.GroupBy(e => e.TweakId))
        {
            var relevant = g.Where(e => e.Status != ActionStatus.BlockedByOS).ToList();
            int ok = relevant.Count(e => e.Status is ActionStatus.Verified or ActionStatus.AlreadyApplied or ActionStatus.Applied);
            map[g.Key] = relevant.Count == 0 ? TweakState.Unknown : ok == relevant.Count ? TweakState.On : ok == 0 ? TweakState.Off : TweakState.Partial;
        }
        return map;
    }

    public Report Apply(IReadOnlyList<Tweak> tweaks) => Run(tweaks, (e, t) => e.Apply(t));
    public Report Revert(IReadOnlyList<Tweak> tweaks) => Run(tweaks, (e, t) => e.Revert(t));

    private Report Run(IReadOnlyList<Tweak> tweaks, Func<TweakEngine, IReadOnlyList<Tweak>, Report> op)
    {
        lock (_gate)
        {
            var store = new JsonRunStore(DataDir);
            store.SecureRoot();
            if (!RunLock.TryAcquire(DataDir, out var lk, out var holder))
                throw new InvalidOperationException($"Другой прогон уже выполняется ({holder}).");
            using (lk) return op(NewEngine(store), tweaks);
        }
    }

    public Report? LastReport() => new JsonRunStore(DataDir).LoadLatestReport();

    // ----- Proof Report -----
    public string BeforePath => Path.Combine(ProofDir, "before.json");
    public string AfterPath => Path.Combine(ProofDir, "after.json");
    public string HtmlPath => Path.Combine(ProofDir, "proof.html");

    public Snapshot Measure(string label, string file)
    {
        var snap = SystemProbe.Take(label);
        Directory.CreateDirectory(ProofDir);
        File.WriteAllText(file, Proof.Serialize(snap));
        return snap;
    }

    public Snapshot? LoadSnapshot(string file)
    {
        try { return File.Exists(file) ? Proof.Deserialize(File.ReadAllText(file)) : null; }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { return null; }
    }

    public void WriteHtml(Snapshot before, Snapshot after) =>
        File.WriteAllText(HtmlPath, Proof.ToHtml(before, after));
}
