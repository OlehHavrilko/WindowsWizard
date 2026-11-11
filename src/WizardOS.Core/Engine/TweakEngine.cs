namespace WizardOS.Core.Engine;

public sealed class TweakEngine : ITweakEngine
{
    public const string FormatVersion = "1";
    private readonly EngineEnv _env;
    private readonly IRunStore _store;
    private readonly string _runId;

    public TweakEngine(EngineEnv env, IRunStore store, string runId)
    {
        _env = env ?? throw new ArgumentNullException(nameof(env));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("runId is empty", nameof(runId));
        _runId = runId;
    }

    // ---------- представление состояния ----------
    private static string Fmt(RegValue v) => v.Exists ? $"{v.Kind}:{v.Data}" : "<missing>";
    private static string Fmt(ServiceState s) => s.Exists ? s.Start.ToString() : "<missing>";

    private static bool Same(RegValue a, RegValue b)
    {
        if (a.Exists != b.Exists) return false;
        if (!a.Exists) return true;
        if (a.Kind != b.Kind) return false;
        return a.Kind == RegKind.Binary
            ? string.Equals(a.Data ?? "", b.Data ?? "", StringComparison.OrdinalIgnoreCase)
            : string.Equals(a.Data ?? "", b.Data ?? "", StringComparison.Ordinal);
    }

    private static bool Same(ServiceState a, ServiceState b)
        => a.Exists == b.Exists && (!a.Exists || a.Start == b.Start);

    private static string TargetOf(TweakAction a) => a.Registry != null
        ? $"{a.Registry.Path}\\{a.Registry.Name}"
        : $"service:{a.Service!.Name}";

    private static RegValue ExpectedReg(RegistryTarget r) => new(true, r.Kind, r.Data);
    private static ServiceState ExpectedSvc(ServiceTarget s) => new(true, s.Start);

    private string CurrentStr(TweakAction a) => a.Registry != null
        ? Fmt(_env.Registry.Read(a.Registry.Path, a.Registry.Name))
        : Fmt(_env.Services.Read(a.Service!.Name));

    private static string ExpectedStr(TweakAction a) => a.Registry != null ? Fmt(ExpectedReg(a.Registry)) : Fmt(ExpectedSvc(a.Service!));

    private bool MatchesExpected(TweakAction a) => a.Registry != null
        ? Same(_env.Registry.Read(a.Registry.Path, a.Registry.Name), ExpectedReg(a.Registry))
        : Same(_env.Services.Read(a.Service!.Name), ExpectedSvc(a.Service!));

    private Report MakeReport(string cmd, List<ReportEntry> entries) => new(FormatVersion, _runId, cmd, _env.Now(), entries);

    private void Journal(ReportEntry e) => _store.SaveJournalLine(_runId, e);

    // ---------- Plan ----------
    public Plan Plan(IReadOnlyList<Tweak> tweaks)
    {
        var entries = new List<PlanEntry>();
        foreach (var t in tweaks)
            foreach (var a in t.Actions)
            {
                string before;
                try { before = CurrentStr(a); } catch (Exception ex) { before = "<error: " + ex.Message + ">"; }
                entries.Add(new PlanEntry(t.Id, a.Id, TargetOf(a), before, ExpectedStr(a)));
            }
        return new Plan(FormatVersion, _runId, _env.Now(), entries);
    }

    // ---------- Apply ----------
    public Report Apply(IReadOnlyList<Tweak> tweaks)
    {
        var entries = new List<ReportEntry>();
        var actions = tweaks.SelectMany(t => t.Actions.Select(a => (t, a))).ToList();

        // Preflight + baseline всех целей до первой записи.
        try
        {
            _store.LoadLatestBaseline(); // хранилище читается
            var be = new List<BaselineEntry>();
            foreach (var (t, a) in actions)
                be.Add(a.Registry != null
                    ? new BaselineEntry(t.Id, a.Id, _env.Registry.Read(a.Registry.Path, a.Registry.Name), null)
                    : new BaselineEntry(t.Id, a.Id, null, _env.Services.Read(a.Service!.Name)));
            _store.SaveBaseline(new Baseline(FormatVersion, _runId, _env.Now(), be)); // хранилище пишется
        }
        catch (Exception ex)
        {
            entries.Add(new ReportEntry("", "preflight", ActionStatus.Failed, "", "", "baseline сохранён", "Preflight: " + ex.Message));
            foreach (var (t, a) in actions)
                entries.Add(new ReportEntry(t.Id, a.Id, ActionStatus.Skipped, "", "", ExpectedStr(a), "Пропущено: preflight не пройден"));
            return Finish("apply", entries);
        }

        bool failed = false;
        foreach (var (t, a) in actions)
        {
            if (failed)
            {
                var sk = new ReportEntry(t.Id, a.Id, ActionStatus.Skipped, "", "", ExpectedStr(a), "Пропущено после сбоя");
                entries.Add(sk); TryJournal(sk);
                continue;
            }
            string expected = ExpectedStr(a), before = "";
            try
            {
                before = CurrentStr(a);
                Journal(new ReportEntry(t.Id, a.Id, ActionStatus.Started, before, "", expected));
                ReportEntry res;
                if (MatchesExpected(a))
                    res = new ReportEntry(t.Id, a.Id, ActionStatus.AlreadyApplied, before, before, expected);
                else
                {
                    Write(a);
                    var after = CurrentStr(a);
                    res = MatchesExpected(a)
                        ? new ReportEntry(t.Id, a.Id, ActionStatus.Applied, before, after, expected)
                        : new ReportEntry(t.Id, a.Id, ActionStatus.Failed, before, after, expected, "После записи значение не совпало с ожидаемым");
                }
                entries.Add(res); Journal(res);
                if (res.Status == ActionStatus.Failed) failed = true;
            }
            catch (Exception ex)
            {
                var res = new ReportEntry(t.Id, a.Id, ActionStatus.Failed, before, "", expected, ex.Message);
                entries.Add(res); TryJournal(res);
                failed = true;
            }
        }
        return Finish("apply", entries);
    }

    private void Write(TweakAction a)
    {
        if (a.Registry != null) _env.Registry.Write(a.Registry.Path, a.Registry.Name, ExpectedReg(a.Registry));
        else _env.Services.SetStartType(a.Service!.Name, a.Service.Start);
    }

    private void TryJournal(ReportEntry e) { try { Journal(e); } catch { } }

    private Report Finish(string cmd, List<ReportEntry> entries)
    {
        var r = MakeReport(cmd, entries);
        try { _store.SaveReport(r); } catch { }
        return r;
    }

    // ---------- Verify ----------
    public Report Verify(IReadOnlyList<Tweak> tweaks)
    {
        var entries = new List<ReportEntry>();
        foreach (var t in tweaks)
            foreach (var a in t.Actions)
            {
                string expected = ExpectedStr(a), cur = "";
                try
                {
                    cur = CurrentStr(a);
                    entries.Add(new ReportEntry(t.Id, a.Id, MatchesExpected(a) ? ActionStatus.Verified : ActionStatus.Mismatch, cur, cur, expected));
                }
                catch (Exception ex)
                {
                    entries.Add(new ReportEntry(t.Id, a.Id, ActionStatus.Mismatch, cur, cur, expected, ex.Message));
                }
            }
        return Finish("verify", entries);
    }

    // ---------- Revert ----------
    public Report Revert(IReadOnlyList<Tweak> tweaks)
    {
        var entries = new List<ReportEntry>();
        Baseline? baseline = null;
        string? loadErr = null;
        try { baseline = _store.LoadLatestBaseline(); } catch (Exception ex) { loadErr = ex.Message; }
        var actions = tweaks.SelectMany(t => t.Actions.Select(a => (t, a))).Reverse().ToList();

        if (baseline == null)
        {
            entries.Add(new ReportEntry("", "baseline", ActionStatus.Failed, "", "", "", loadErr ?? "Baseline не найден, откат без записи"));
            foreach (var (t, a) in actions)
                entries.Add(new ReportEntry(t.Id, a.Id, ActionStatus.Skipped, "", "", ExpectedStr(a), "Пропущено: нет baseline"));
            return Finish("revert", entries);
        }

        foreach (var (t, a) in actions)
        {
            string expected = ExpectedStr(a), cur = "";
            try
            {
                var be = baseline.Entries.FirstOrDefault(e => e.TweakId == t.Id && e.ActionId == a.Id);
                if (be == null || (a.Registry != null ? be.Registry == null : be.Service == null))
                {
                    entries.Add(new ReportEntry(t.Id, a.Id, ActionStatus.Failed, "", "", expected, "В baseline нет записи для цели (неполный снимок), без записи"));
                    continue;
                }
                string baseStr = a.Registry != null ? Fmt(be.Registry!) : Fmt(be.Service!);
                cur = CurrentStr(a);
                bool isBase = a.Registry != null
                    ? Same(_env.Registry.Read(a.Registry.Path, a.Registry.Name), be.Registry!)
                    : Same(_env.Services.Read(a.Service!.Name), be.Service!);
                ReportEntry res;
                if (isBase)
                    res = new ReportEntry(t.Id, a.Id, ActionStatus.NothingToRevert, cur, cur, baseStr);
                else if (MatchesExpected(a))
                {
                    if (a.Service != null && (!be.Service!.Exists || be.Service.Start == StartType.Unknown))
                    {
                        res = new ReportEntry(t.Id, a.Id, ActionStatus.Failed, cur, cur, baseStr, "Baseline службы неполон, восстановление невозможно");
                    }
                    else
                    {
                        if (a.Registry != null) _env.Registry.Write(a.Registry.Path, a.Registry.Name, be.Registry!);
                        else _env.Services.SetStartType(a.Service!.Name, be.Service!.Start);
                        var after = CurrentStr(a);
                        bool ok = a.Registry != null
                            ? Same(_env.Registry.Read(a.Registry.Path, a.Registry.Name), be.Registry!)
                            : Same(_env.Services.Read(a.Service!.Name), be.Service!);
                        res = ok
                            ? new ReportEntry(t.Id, a.Id, ActionStatus.Reverted, cur, after, baseStr)
                            : new ReportEntry(t.Id, a.Id, ActionStatus.Failed, cur, after, baseStr, "После восстановления значение не совпало с baseline");
                    }
                }
                else
                    res = new ReportEntry(t.Id, a.Id, ActionStatus.Conflict, cur, cur, baseStr,
                        $"Текущее значение не совпадает ни с ожидаемым ({expected}), ни с baseline ({baseStr}); не перезаписано");
                entries.Add(res);
                TryJournal(res);
            }
            catch (Exception ex)
            {
                var res = new ReportEntry(t.Id, a.Id, ActionStatus.Failed, cur, "", expected, ex.Message);
                entries.Add(res); TryJournal(res);
            }
        }
        return Finish("revert", entries);
    }
}
