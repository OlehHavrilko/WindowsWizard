using WizardOS.Core;
using WizardOS.Core.Engine;

namespace WizardOS.Core.Tests;

public sealed class FakeRegistry : IRegistryAccess
{
    public Dictionary<(string, string), RegValue> Values = new();
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
        if (!value.Exists) Values.Remove(Key(path, name));
        else Values[Key(path, name)] = value;
    }

    public void Set(string path, string name, RegValue v) => Values[Key(path, name)] = v;
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
        States[name] = new ServiceState(true, start);
    }
}

public sealed class MemStore : IRunStore
{
    public List<Baseline> Baselines = new();
    public List<Report> Reports = new();
    public List<ReportEntry> Journal = new();
    public void SaveBaseline(Baseline b)
    {
        if (Baselines.Count > 0) throw new InvalidOperationException("baseline exists");
        Baselines.Add(b);
    }
    public Baseline? LoadLatestBaseline() => Baselines.LastOrDefault();
    public void SaveReport(Report r) => Reports.Add(r);
    public void SaveJournalLine(string runId, ReportEntry e) => Journal.Add(e);
}

public sealed class Rig
{
    public FakeRegistry Reg = new();
    public FakeServices Svc = new();
    public MemStore Store = new();
    public ITweakEngine Engine;
    public const string K = @"HKLM\SOFTWARE\Test";

    public Rig(IRunStore? store = null)
    {
        var env = new EngineEnv(Reg, Svc, () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Engine = new TweakEngine(env, store ?? Store, "run1");
    }

    public static Tweak T(string id, params TweakAction[] a) =>
        new(id, id, Verdict.Core, Stability.Stable, "intent", "evidence", a);
    public static TweakAction Dw(string id, string name, int v, string path = K) =>
        new(id, Registry: new RegistryTarget(path, name, RegKind.DWord, v.ToString()));
    public static TweakAction Svc_(string id, string name, StartType s) =>
        new(id, Service: new ServiceTarget(name, s));
    public static RegValue D(int v) => new(true, RegKind.DWord, v.ToString());
}
