using BareOS.Core;
using BareOS.Core.Engine;

namespace BareOS.Core.Tests;

public sealed class FakeRegistry : IRegistryAccess
{
    public Dictionary<(string, string), RegValue> Values = new();
    public HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DeletedKeys = new();
    public List<string> Writes = new();
    /// <summary>Если вернёт true — запись падает исключением.</summary>
    public Func<string, string, RegValue, bool>? FailWhen;

    static (string, string) Key(string p, string n) => (p.ToUpperInvariant(), n.ToLowerInvariant());

    public RegValue Read(string path, string name) =>
        Values.TryGetValue(Key(path, name), out var v) ? v : RegValue.Missing;

    public void Write(string path, string name, RegValue value)
    {
        if (FailWhen?.Invoke(path, name, value) == true)
            throw new IOException($"injected write failure {path}\\{name}");
        Writes.Add($"{path}|{name}");
        if (value.Exists) Keys.Add(path);
        if (!value.Exists) Values.Remove(Key(path, name));
        else Values[Key(path, name)] = value;
    }

    public bool KeyExists(string path) => Keys.Contains(path) || Values.Keys.Any(k => k.Item1 == path.ToUpperInvariant());

    public void DeleteKeyIfEmpty(string path)
    {
        if (Values.Keys.Any(k => k.Item1 == path.ToUpperInvariant())) return;
        if (Keys.Remove(path)) DeletedKeys.Add(path);
    }

    public void Set(string path, string name, RegValue v) { Values[Key(path, name)] = v; Keys.Add(path); }
    public void SetDword(string path, string name, int d) => Set(path, name, new RegValue(true, RegKind.DWord, d.ToString()));
}

public sealed class FakeServices : IServiceAccess
{
    public Dictionary<string, ServiceState> States = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Writes = new();
    public Func<string, StartType, bool>? FailWhen;

    public ServiceState Read(string name) => States.TryGetValue(name, out var s) ? s : new ServiceState(false);

    public void SetStartType(string name, StartType start)
    {
        if (FailWhen?.Invoke(name, start) == true) throw new IOException("injected service failure " + name);
        Writes.Add($"{name}|{start}");
        States[name] = (States.TryGetValue(name, out var old) ? old : new ServiceState(true)) with { Start = start };
    }

    public void StopService(string name)
    {
        Writes.Add($"stop:{name}");
        States[name] = States[name] with { Running = false };
    }

    public void StartService(string name)
    {
        Writes.Add($"start:{name}");
        States[name] = States[name] with { Running = true };
    }
}

public sealed class FakeTasks : ITaskAccess
{
    public Dictionary<string, TaskState> States = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Writes = new();
    public Func<string, bool, bool>? FailWhen;

    public TaskState Read(string path) => States.TryGetValue(path, out var s) ? s : new TaskState(false);

    public void SetEnabled(string path, bool enabled)
    {
        if (FailWhen?.Invoke(path, enabled) == true) throw new IOException("injected task failure " + path);
        Writes.Add($"{path}|{enabled}");
        States[path] = new TaskState(true, enabled);
    }
}

public sealed class MemStore : IRunStore
{
    public List<Baseline> Baselines = new();
    public List<Report> Reports = new();
    public List<ReportEntry> Journal = new();
    public void SaveBaseline(Baseline b)
    {
        if (Baselines.Any(x => x.BaselineId == b.BaselineId)) throw new InvalidOperationException("baseline exists");
        Baselines.Add(b);
    }
    public Baseline? LoadLatestBaseline() => Baselines.LastOrDefault();
    public IReadOnlyList<Baseline> LoadBaselines() => Baselines.ToList();
    public void SaveReport(Report r) => Reports.Add(r);
    public void SaveJournalLine(string runId, ReportEntry e) => Journal.Add(e);
}

public sealed class Rig
{
    public FakeRegistry Reg = new();
    public FakeServices Svc = new();
    public FakeTasks Tasks = new();
    public MemStore Store = new();
    public ITweakEngine Engine;
    public const string K = @"HKLM\SOFTWARE\Test";

    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly IRunStore _store;

    public Rig(IRunStore? store = null)
    {
        _store = store ?? Store;
        Engine = Make("run1");
    }

    private ITweakEngine Make(string runId)
        => new TweakEngine(new EngineEnv(Reg, Svc, () => _now, Tasks), _store, runId);

    /// <summary>Новый прогон (новый процесс bareos) на том же хранилище и той же «системе», на минуту позже.</summary>
    public ITweakEngine NextRun(string runId)
    {
        _now = _now.AddMinutes(1);
        return Make(runId);
    }

    public static Tweak T(string id, params TweakAction[] a) =>
        new(id, id, Verdict.Core, Stability.Stable, "intent", "evidence", a);
    public static TweakAction Dw(string id, string name, int v, string path = K) =>
        new(id, Registry: new RegistryTarget(path, name, RegKind.DWord, v.ToString()));
    public static TweakAction Svc_(string id, string name, StartType s) =>
        new(id, Service: new ServiceTarget(name, s));
    public static TweakAction Task_(string id, string path, bool enabled) =>
        new(id, Task: new TaskTarget(path, enabled));
    public static RegValue D(int v) => new(true, RegKind.DWord, v.ToString());
}
