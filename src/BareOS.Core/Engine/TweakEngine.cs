namespace BareOS.Core.Engine;

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

    private static string Fmt(TaskState t) => t.Exists ? (t.Enabled ? "Enabled" : "Disabled") : "<missing>";
    private static bool Same(TaskState a, TaskState b)
        => a.Exists == b.Exists && (!a.Exists || a.Enabled == b.Enabled);
    /// <summary>Ожидание для Enabled=false выполнено и при отсутствии задачи (в этой сборке её нет — ничего не запускается).</summary>
    private static bool Satisfies(TaskState cur, TaskTarget t)
        => t.Enabled ? cur.Exists && cur.Enabled : !cur.Exists || !cur.Enabled;

    private ITaskAccess Tasks => _env.Tasks ?? throw new InvalidOperationException("Обработчик task не настроен (ITaskAccess)");

    private static string TargetOf(TweakAction a) => a.Registry != null
        ? $"{a.Registry.Path}\\{a.Registry.Name}"
        : a.Service != null ? $"service:{a.Service.Name}" : $"task:{a.Task!.Path}";

    private static RegValue ExpectedReg(RegistryTarget r) => new(true, r.Kind, r.Data);
    private static ServiceState ExpectedSvc(ServiceTarget s) => new(true, s.Start);

    private string CurrentStr(TweakAction a) =>
        a.Registry != null ? Fmt(_env.Registry.Read(a.Registry.Path, a.Registry.Name))
        : a.Service != null ? Fmt(_env.Services.Read(a.Service.Name))
        : Fmt(Tasks.Read(a.Task!.Path));

    private static string ExpectedStr(TweakAction a) =>
        a.Registry != null ? Fmt(ExpectedReg(a.Registry))
        : a.Service != null ? Fmt(ExpectedSvc(a.Service))
        : (a.Task!.Enabled ? "Enabled" : "Disabled (или <missing>)");

    private bool MatchesExpected(TweakAction a) =>
        a.Registry != null ? Same(_env.Registry.Read(a.Registry.Path, a.Registry.Name), ExpectedReg(a.Registry))
        : a.Service != null ? Same(_env.Services.Read(a.Service.Name), ExpectedSvc(a.Service))
        : Satisfies(Tasks.Read(a.Task!.Path), a.Task);

    private BaselineEntry Capture(string tweakId, TweakAction a) =>
        a.Registry != null ? new BaselineEntry(tweakId, a.Id, _env.Registry.Read(a.Registry.Path, a.Registry.Name), null, null, _env.Registry.KeyExists(a.Registry.Path))
        : a.Service != null ? new BaselineEntry(tweakId, a.Id, null, _env.Services.Read(a.Service.Name))
        : new BaselineEntry(tweakId, a.Id, null, null, Tasks.Read(a.Task!.Path));

    private static bool HasState(BaselineEntry e, TweakAction a)
        => a.Registry != null ? e.Registry != null : a.Service != null ? e.Service != null : e.Task != null;

    private static bool CapturedExpected(BaselineEntry e, TweakAction a)
        => a.Registry != null ? Same(e.Registry!, ExpectedReg(a.Registry))
         : a.Service != null ? Same(e.Service!, ExpectedSvc(a.Service))
         : Satisfies(e.Task!, a.Task!);

    /// <summary>Снимок цели для отката: самый новый, в котором цель ещё НЕ была в ожидаемом состоянии
    /// (т. е. сделанный перед тем, как движок её изменил). Если во всех снимках цель уже была ожидаемой —
    /// самый новый (откатывать нечего). Повторный apply и apply по частям (--only) так не теряют исходное.</summary>
    private static BaselineEntry? PickBaseline(IReadOnlyList<Baseline> oldestFirst, string tweakId, TweakAction a)
    {
        BaselineEntry? newest = null;
        for (int i = oldestFirst.Count - 1; i >= 0; i--)
        {
            var e = oldestFirst[i].Entries.FirstOrDefault(x => x.TweakId == tweakId && x.ActionId == a.Id);
            if (e == null || !HasState(e, a)) continue;
            if (oldestFirst[i].IsRevertMarker) return newest ?? e;   // граница: до неё всё уже откатано
            newest ??= e;
            if (!CapturedExpected(e, a)) return e;
        }
        return newest;
    }

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
            _store.LoadBaselines(); // хранилище читается (битый baseline = стоп до записи)
            var be = new List<BaselineEntry>();
            foreach (var (t, a) in actions)
                be.Add(Capture(t.Id, a));
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
        else if (a.Service != null)
        {
            var wasRunning = _env.Services.Read(a.Service.Name).Running;
            _env.Services.SetStartType(a.Service.Name, a.Service.Start);
            // Отключённая служба продолжала бы работать до перезагрузки: останавливаем (best-effort, результат apply не зависит от остановки).
            if (a.Service.Start == StartType.Disabled && wasRunning)
                try { _env.Services.StopService(a.Service.Name); } catch (Exception) { /* служба остановится при перезагрузке */ }
        }
        else Tasks.SetEnabled(a.Task!.Path, a.Task.Enabled);
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
        IReadOnlyList<Baseline> baselines = Array.Empty<Baseline>();
        string? loadErr = null;
        try { baselines = _store.LoadBaselines(); } catch (Exception ex) { loadErr = ex.Message; }
        var actions = tweaks.SelectMany(t => t.Actions.Select(a => (t, a))).Reverse().ToList();

        if (baselines.Count == 0)
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
                var be = PickBaseline(baselines, t.Id, a);
                if (be == null)
                {
                    // apply снимает baseline ВСЕХ своих целей до первой записи, поэтому цель без записи движок не менял
                    // (карточка proposed, добавленная в каталог позже и т. п.): откатывать нечего.
                    var curNone = CurrentStr(a);
                    var nr = new ReportEntry(t.Id, a.Id, ActionStatus.NothingToRevert, curNone, curNone, "", "В baseline нет записи: движок эту цель не менял");
                    entries.Add(nr); TryJournal(nr);
                    continue;
                }
                string baseStr = a.Registry != null ? Fmt(be.Registry!) : a.Service != null ? Fmt(be.Service!) : Fmt(be.Task!);
                cur = CurrentStr(a);
                bool IsBaseNow() => a.Registry != null
                    ? Same(_env.Registry.Read(a.Registry.Path, a.Registry.Name), be.Registry!)
                    : a.Service != null ? Same(_env.Services.Read(a.Service.Name), be.Service!)
                    : Same(Tasks.Read(a.Task!.Path), be.Task!);
                bool isBase = IsBaseNow();
                ReportEntry res;
                string? keyNote = null;
                if (isBase)
                    res = new ReportEntry(t.Id, a.Id, ActionStatus.NothingToRevert, cur, cur, baseStr);
                else if (MatchesExpected(a))
                {
                    if (a.Service != null && (!be.Service!.Exists || be.Service.Start is StartType.Unknown))
                    {
                        res = new ReportEntry(t.Id, a.Id, ActionStatus.Failed, cur, cur, baseStr, "Baseline службы неполон, восстановление невозможно");
                    }
                    else if (a.Task != null && !be.Task!.Exists)
                    {
                        res = new ReportEntry(t.Id, a.Id, ActionStatus.Failed, cur, cur, baseStr, "Задачи не было в baseline, восстановление невозможно");
                    }
                    else
                    {
                        if (a.Registry != null)
                        {
                            _env.Registry.Write(a.Registry.Path, a.Registry.Name, be.Registry!);
                            if (!be.Registry!.Exists && be.KeyExisted == false)   // ключ создал apply
                            {
                                try { _env.Registry.DeleteKeyIfEmpty(a.Registry.Path); }
                                catch (Exception ex) { keyNote = "Значение восстановлено, но пустой ключ не удалён: " + ex.Message; }
                            }
                        }
                        else if (a.Service != null)
                        {
                            _env.Services.SetStartType(a.Service.Name, be.Service!.Start);
                            // до apply служба работала, а apply её остановил: запускаем обратно (best-effort)
                            if (be.Service.Running && be.Service.Start != StartType.Disabled && !_env.Services.Read(a.Service.Name).Running)
                                try { _env.Services.StartService(a.Service.Name); } catch (Exception) { /* запустится при перезагрузке (тип запуска восстановлен) */ }
                        }
                        else Tasks.SetEnabled(a.Task!.Path, be.Task!.Enabled);
                        var after = CurrentStr(a);
                        bool ok = IsBaseNow();
                        res = ok
                            ? new ReportEntry(t.Id, a.Id, ActionStatus.Reverted, cur, after, baseStr, keyNote)
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
        SaveRevertMarker(tweaks, entries);
        return Finish("revert", entries);
    }

    /// <summary>После отката фиксируем состояние откатанных целей маркером-снимком (см. Baseline.IsRevertMarker).</summary>
    private void SaveRevertMarker(IReadOnlyList<Tweak> tweaks, List<ReportEntry> entries)
    {
        try
        {
            var done = entries.Where(e => e.Status == ActionStatus.Reverted).Select(e => (e.TweakId, e.ActionId)).ToHashSet();
            if (done.Count == 0) return;
            var be = new List<BaselineEntry>();
            foreach (var t in tweaks)
                foreach (var a in t.Actions)
                    if (done.Contains((t.Id, a.Id))) be.Add(Capture(t.Id, a));
            _store.SaveBaseline(new Baseline(FormatVersion, _runId + "-revert", _env.Now(), be, IsRevertMarker: true));
        }
        catch { /* маркер вспомогательный: сбой не должен ломать отчёт об откате */ }
    }
}
