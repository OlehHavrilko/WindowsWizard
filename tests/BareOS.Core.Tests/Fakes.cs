using BareOS.Core;
using BareOS.Core.Engine;

namespace BareOS.Core.Tests;

public sealed class FakeRegistry : IRegistryAccess
{
    public Dictionary<(string, string), RegValue> Values = new();
    public HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DeletedKeys = new();
    public bool FailDeleteKey;
    public List<string> Writes = new();
    /// <summary>Если вернёт true — запись падает исключением.</summary>
    public Func<string, string, RegValue, bool>? FailWhen;

    /// <summary>Пути, запись в которые запрещает «ОС» (как UCPD): Write бросает UnauthorizedAccessException.</summary>
    public HashSet<string> DenyWriteKeys = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Пути, удаление которых запрещено «ОС»: DeleteKeyIfEmpty бросает UnauthorizedAccessException.</summary>
    public HashSet<string> DenyDeleteKeys = new(StringComparer.OrdinalIgnoreCase);

    static (string, string) Key(string p, string n) => (p.ToUpperInvariant(), n.ToLowerInvariant());

    public RegValue Read(string path, string name) =>
        Values.TryGetValue(Key(path, name), out var v) ? v : RegValue.Missing;

    public void Write(string path, string name, RegValue value)
    {
        if (FailWhen?.Invoke(path, name, value) == true)
            throw new IOException($"injected write failure {path}\\{name}");
        if (DenyWriteKeys.Contains(path)) throw new UnauthorizedAccessException($"Access to the registry key '{path}' is denied.");
        Writes.Add($"{path}|{name}");
        if (value.Exists) Keys.Add(path);
        if (!value.Exists) Values.Remove(Key(path, name));
        else Values[Key(path, name)] = value;
    }

    public bool KeyExists(string path) => Keys.Contains(path) || Values.Keys.Any(k => k.Item1 == path.ToUpperInvariant());

    public void DeleteKeyIfEmpty(string path)
    {
        if (DenyDeleteKeys.Contains(path)) throw new UnauthorizedAccessException($"Access to the registry key '{path}' is denied.");
        if (FailDeleteKey) throw new UnauthorizedAccessException("injected key delete failure");
        if (Values.Keys.Any(k => k.Item1 == path.ToUpperInvariant())) return;
        if (Keys.Remove(path)) DeletedKeys.Add(path);
    }

    public void Set(string path, string name, RegValue v) { Values[Key(path, name)] = v; Keys.Add(path); }
    public void SetDword(string path, string name, int d) => Set(path, name, new RegValue(true, RegKind.DWord, d.ToString()));

    /// <summary>Запись как если бы её сделал другой процесс (gpsvc после gpupdate): минует DenyWriteKeys/FailWhen,
    /// которые симулируют отказ именно нашему процессу, не системе в целом.</summary>
    public void ForceWrite(string path, string name, RegValue value)
    {
        Writes.Add($"{path}|{name}|policy");
        if (value.Exists) { Keys.Add(path); Values[Key(path, name)] = value; }
        else Values.Remove(Key(path, name));
    }
}

/// <summary>Фальшивая групповая политика (D-031): по умолчанию «успешно пишет» прямо в реестр, минуя DenyWriteKeys,
/// — так в тестах моделируется, что запись через gpupdate обходит отказ, который получает только наш процесс.</summary>
public sealed class FakePolicyWriter : IPolicyWriter
{
    private readonly FakeRegistry _registry;
    public List<(string Path, string Name, RegValue Value)> Calls = new();
    public bool Succeeds = true;

    public FakePolicyWriter(FakeRegistry registry) => _registry = registry;

    public bool TrySet(string path, string name, RegValue value)
    {
        Calls.Add((path, name, value));
        if (!Succeeds) return false;
        _registry.ForceWrite(path, name, value);
        return true;
    }
}

public sealed class FakeAppx : IAppxAccess
{
    public List<string> Installed = new();      // имена пакетов (PackageFullName без префикса)
    public List<string> Removed = new();
    public Func<string, bool>? FailWhen;

    private static bool Like(string name, string pattern)
        => System.Text.RegularExpressions.Regex.IsMatch(name, "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public IReadOnlyList<string> Find(string namePattern)
        => Installed.Where(n => Like(n, namePattern)).Select(n => "installed:" + n).ToList();

    public void Remove(string namePattern)
    {
        if (FailWhen?.Invoke(namePattern) == true) throw new IOException("injected appx failure " + namePattern);
        Removed.Add(namePattern);
        Installed.RemoveAll(n => Like(n, namePattern));
    }
}

public sealed class FakeServices : IServiceAccess
{
    public Dictionary<string, ServiceState> States = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Writes = new();
    public Func<string, StartType, bool>? FailWhen;

    /// <summary>Чтение состояния службы падает (проверить UCPD нельзя).</summary>
    public bool ThrowOnRead;

    public ServiceState Read(string name)
    {
        if (ThrowOnRead) throw new IOException("injected service read failure " + name);
        return States.TryGetValue(name, out var s) ? s : new ServiceState(false);
    }

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
    public FakeAppx Appx = new();
    public MemStore Store = new();
    public ITweakEngine Engine;
    public const string K = @"HKLM\SOFTWARE\Test";

    /// <summary>Обход UCPD через групповую политику (D-031). Null в большинстве тестов: фолбэк выключен,
    /// блокировка остаётся BlockedByOS, как до D-031.</summary>
    public FakePolicyWriter? Policy;

    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly IRunStore _store;

    public Rig(IRunStore? store = null)
    {
        _store = store ?? Store;
        Engine = Make("run1");
    }

    private ITweakEngine Make(string runId)
        => new TweakEngine(new EngineEnv(Reg, Svc, () => _now, Tasks, Policy, Appx), _store, runId);

    /// <summary>Новый прогон (новый процесс bareos) на том же хранилище и той же «системе», на минуту позже.</summary>
    public ITweakEngine NextRun(string runId)
    {
        _now = _now.AddMinutes(1);
        return Make(runId);
    }

    /// <summary>Включает обход UCPD через групповую политику (D-031) и пересоздаёт Engine с тем же runId.</summary>
    public Rig WithPolicy()
    {
        Policy = new FakePolicyWriter(Reg);
        Engine = Make("run1");
        return this;
    }

    public static Tweak T(string id, params TweakAction[] a) =>
        new(id, id, Verdict.Core, Stability.Stable, "intent", "evidence", a);
    public static TweakAction Dw(string id, string name, int v, string path = K) =>
        new(id, Registry: new RegistryTarget(path, name, RegKind.DWord, v.ToString()));
    public static TweakAction Svc_(string id, string name, StartType s) =>
        new(id, Service: new ServiceTarget(name, s));
    public static TweakAction Task_(string id, string path, bool enabled) =>
        new(id, Task: new TaskTarget(path, enabled));
    public static TweakAction Appx_(string id, string name) =>
        new(id, Appx: new AppxTarget(name));
    public static RegValue D(int v) => new(true, RegKind.DWord, v.ToString());
}
