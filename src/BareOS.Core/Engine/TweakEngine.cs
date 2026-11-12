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

    /// <summary>Для Disabled отсутствие службы в этой сборке считается выполненным (ничего не запускается), как и для задач.</summary>
    private static bool SvcSatisfied(ServiceState cur, ServiceTarget t)
        => (!cur.Exists && t.Start == StartType.Disabled) || Same(cur, ExpectedSvc(t));

    private static string Fmt(TaskState t) => t.Exists ? (t.Enabled ? "Enabled" : "Disabled") : "<missing>";
    private static bool Same(TaskState a, TaskState b)
        => a.Exists == b.Exists && (!a.Exists || a.Enabled == b.Enabled);
    /// <summary>Ожидание для Enabled=false выполнено и при отсутствии задачи (в этой сборке её нет — ничего не запускается).</summary>
    private static bool Satisfies(TaskState cur, TaskTarget t)
        => t.Enabled ? cur.Exists && cur.Enabled : !cur.Exists || !cur.Enabled;

    private IAppxAccess Appx => _env.Appx ?? throw new InvalidOperationException("Обработчик appx не настроен (IAppxAccess)");
    private static string FmtAppx(IReadOnlyList<string> found) => found.Count == 0 ? "<absent>" : "present: " + string.Join(", ", found);

    private IFileAccess Files => _env.Files ?? throw new InvalidOperationException("Обработчик file не настроен (IFileAccess)");
    private static string Short(string? h) => h is null ? "" : h[..Math.Min(12, h.Length)];
    private static string Fmt(FileState f) => f.Exists ? "sha256:" + Short(f.Sha256) : "<missing>";
    private static bool Same(FileState a, FileState b)
        => a.Exists == b.Exists && (!a.Exists || string.Equals(a.Sha256, b.Sha256, StringComparison.OrdinalIgnoreCase));

    private ITaskAccess Tasks => _env.Tasks ?? throw new InvalidOperationException("Обработчик task не настроен (ITaskAccess)");

    private static string TargetOf(TweakAction a) => a.Registry != null
        ? $"{a.Registry.Path}\\{a.Registry.Name}"
        : a.Service != null ? $"service:{a.Service.Name}" : a.Task != null ? $"task:{a.Task.Path}" : a.File != null ? $"file:{a.File.Dest}" : $"appx:{a.Appx!.Name}";

    private static RegValue ExpectedReg(RegistryTarget r) => new(true, r.Kind, r.Data);
    private static ServiceState ExpectedSvc(ServiceTarget s) => new(true, s.Start);

    private string CurrentStr(TweakAction a) =>
        a.Registry != null ? Fmt(_env.Registry.Read(a.Registry.Path, a.Registry.Name))
        : a.Service != null ? Fmt(_env.Services.Read(a.Service.Name))
        : a.Task != null ? Fmt(Tasks.Read(a.Task.Path))
        : a.File != null ? Fmt(Files.Read(a.File.Dest))
        : FmtAppx(Appx.Find(a.Appx!.Name));

    private string ExpectedStr(TweakAction a) =>
        a.Registry != null ? Fmt(ExpectedReg(a.Registry))
        : a.Service != null ? (a.Service.Start == StartType.Disabled ? "Disabled (или <missing>)" : Fmt(ExpectedSvc(a.Service)))
        : a.Task != null ? (a.Task.Enabled ? "Enabled" : "Disabled (или <missing>)")
        : a.File != null ? "sha256:" + Short(SourceHashSafe(a.File))
        : "<absent>";

    private string? SourceHashSafe(FileTarget f) { try { return _env.Files?.SourceHash(f.Source); } catch (Exception) { return "<нет ресурса>"; } }

    private bool MatchesExpected(TweakAction a) =>
        a.Registry != null ? Same(_env.Registry.Read(a.Registry.Path, a.Registry.Name), ExpectedReg(a.Registry))
        : a.Service != null ? SvcSatisfied(_env.Services.Read(a.Service.Name), a.Service)
        : a.Task != null ? Satisfies(Tasks.Read(a.Task.Path), a.Task)
        : a.File != null ? FileMatches(Files.Read(a.File.Dest), a.File)
        : Appx.Find(a.Appx!.Name).Count == 0;

    private bool FileMatches(FileState cur, FileTarget t)
        => cur.Exists && string.Equals(cur.Sha256, Files.SourceHash(t.Source), StringComparison.OrdinalIgnoreCase);

    private BaselineEntry CaptureFile(string tweakId, TweakAction a, bool backup)
    {
        var cur = Files.Read(a.File!.Dest);
        string? bak = null;
        if (backup && cur.Exists && !FileMatches(cur, a.File))
            bak = Files.Backup(a.File.Dest, _runId + "-" + tweakId + "-" + a.Id);
        return new BaselineEntry(tweakId, a.Id, null, null, null, null, null, cur with { BackupPath = bak });
    }

    private BaselineEntry Capture(string tweakId, TweakAction a, bool backup = true) =>
        a.File != null ? CaptureFile(tweakId, a, backup) :
        a.Registry != null ? new BaselineEntry(tweakId, a.Id, _env.Registry.Read(a.Registry.Path, a.Registry.Name), null, null, _env.Registry.KeyExists(a.Registry.Path))
        : a.Service != null ? new BaselineEntry(tweakId, a.Id, null, _env.Services.Read(a.Service.Name))
        : a.Task != null ? new BaselineEntry(tweakId, a.Id, null, null, Tasks.Read(a.Task.Path))
        : new BaselineEntry(tweakId, a.Id, null, null, null, null, string.Join("; ", Appx.Find(a.Appx!.Name)));

    private static bool HasState(BaselineEntry e, TweakAction a)
        => a.Registry != null ? e.Registry != null : a.Service != null ? e.Service != null : a.Task != null ? e.Task != null : a.File != null && e.File != null;

    private bool CapturedExpected(BaselineEntry e, TweakAction a)
        => a.Registry != null ? Same(e.Registry!, ExpectedReg(a.Registry))
         : a.Service != null ? SvcSatisfied(e.Service!, a.Service)
         : a.Task != null ? Satisfies(e.Task!, a.Task)
         : a.File != null && e.File!.Exists && string.Equals(e.File.Sha256, Files.SourceHash(a.File.Source), StringComparison.OrdinalIgnoreCase);

    /// <summary>Снимок цели для отката: самый новый, в котором цель ещё НЕ была в ожидаемом состоянии
    /// (т. е. сделанный перед тем, как движок её изменил). Если во всех снимках цель уже была ожидаемой —
    /// самый новый (откатывать нечего). Повторный apply и apply по частям (--only) так не теряют исходное.</summary>
    private BaselineEntry? PickBaseline(IReadOnlyList<Baseline> oldestFirst, string tweakId, TweakAction a)
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
                if (a.Registry != null && TryPolicyFallback(a.Registry.Path, a.Registry.Name, ExpectedReg(a.Registry), ex))
                {
                    var after = CurrentStr(a);
                    var viaPolicy = new ReportEntry(t.Id, a.Id, ActionStatus.Applied, before, after, expected,
                        "Применено через локальную групповую политику: прямую запись блокирует UCPD (D-031)");
                    entries.Add(viaPolicy); TryJournal(viaPolicy);
                    continue;
                }
                var blocked = Blocked(t, a, ex, before, expected, ex.Message);
                if (blocked is not null) { entries.Add(blocked); TryJournal(blocked); continue; }   // блокировка ОС не сбой движка: остальные действия выполняются
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
        else if (a.Task != null) Tasks.SetEnabled(a.Task.Path, a.Task.Enabled);
        else if (a.File != null) Files.Copy(a.File.Source, a.File.Dest);
        else Appx.Remove(a.Appx!.Name);
    }

    /// <summary>BlockedByOS, если отказ в реестровом действии вызван UCPD (см. <see cref="UcpdGuard"/>); иначе null.
    /// <paramref name="error"/> = null — отказа не было (verify).</summary>
    private ReportEntry? Blocked(Tweak t, TweakAction a, Exception? error, string before, string expected, string? detail)
    {
        if (a.Registry is null || !UcpdGuard.IsBlockedByOs(_env.Services, a.Registry.Path, a.Registry.Name, error)) return null;
        return new ReportEntry(t.Id, a.Id, ActionStatus.BlockedByOS, before, before, expected, UcpdGuard.Explain(a.Registry.Path, a.Registry.Name, detail));
    }

    private void TryJournal(ReportEntry e) { try { Journal(e); } catch { } }

    /// <summary>Обход блокировки UCPD через локальную групповую политику (D-031): только если отказ — именно
    /// блокировка ОС на известном защищённом ключе и обход включён (EngineEnv.Policy != null).</summary>
    private bool TryPolicyFallback(string path, string name, RegValue value, Exception error)
        => _env.Policy != null
           && UcpdGuard.IsBlockedByOs(_env.Services, path, name, error)
           && _env.Policy.TrySet(path, name, value);

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
                    if (MatchesExpected(a)) { entries.Add(new ReportEntry(t.Id, a.Id, ActionStatus.Verified, cur, cur, expected)); continue; }
                    // Значение не то, а менять его ОС не даёт: проверка неприменима (n/a), не «не совпало».
                    entries.Add(Blocked(t, a, null, cur, expected, "Проверка неприменима (n/a): значение не совпадает с ожидаемым, но записать его нельзя.")
                        ?? new ReportEntry(t.Id, a.Id, ActionStatus.Mismatch, cur, cur, expected));
                }
                catch (Exception ex)
                {
                    entries.Add(Blocked(t, a, ex, cur, expected, "Проверка неприменима (n/a): " + ex.Message)
                        ?? new ReportEntry(t.Id, a.Id, ActionStatus.Mismatch, cur, cur, expected, ex.Message));
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
            if (a.Appx != null)
            {
                // Удалённый Appx из baseline не вернуть (D-033): честно сообщаем, а не делаем вид, что откатили.
                string now;
                try { now = CurrentStr(a); } catch (Exception ex) { now = "<error: " + ex.Message + ">"; }
                var ar = new ReportEntry(t.Id, a.Id, ActionStatus.Skipped, now, now, "",
                    "Appx не восстанавливается откатом (D-033): вернуть приложение можно из Microsoft Store или winget. Baseline: "
                    + (baselines.SelectMany(b => b.Entries).LastOrDefault(e => e.TweakId == t.Id && e.ActionId == a.Id)?.AppxPackages ?? "<нет записи>"));
                entries.Add(ar); TryJournal(ar);
                continue;
            }
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
                string baseStr = a.Registry != null ? Fmt(be.Registry!) : a.Service != null ? Fmt(be.Service!) : a.File != null ? Fmt(be.File!) : Fmt(be.Task!);   // Appx сюда не доходит: обработан выше
                cur = CurrentStr(a);
                bool IsBaseNow() => a.Registry != null
                    ? Same(_env.Registry.Read(a.Registry.Path, a.Registry.Name), be.Registry!)
                    : a.Service != null ? Same(_env.Services.Read(a.Service.Name), be.Service!)
                    : a.File != null ? Same(Files.Read(a.File.Dest), be.File!)
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
                    else if (a.File != null && be.File!.Exists && be.File.BackupPath is null)
                    {
                        res = new ReportEntry(t.Id, a.Id, ActionStatus.Failed, cur, cur, baseStr, "Резервной копии прежнего файла нет, восстановление невозможно");
                    }
                    else
                    {
                        if (a.File != null)
                        {
                            if (be.File!.Exists) Files.Restore(be.File.BackupPath!, a.File.Dest);
                            else Files.Delete(a.File.Dest);
                        }
                        else if (a.Registry != null)
                        {
                            try { _env.Registry.Write(a.Registry.Path, a.Registry.Name, be.Registry!); }
                            catch (Exception wex) when (TryPolicyFallback(a.Registry.Path, a.Registry.Name, be.Registry!, wex))
                            {
                                // восстановлено через локальную групповую политику (D-031)
                            }
                            if (!be.Registry!.Exists && be.KeyExisted == false)   // ключ создал apply
                            {
                                try { _env.Registry.DeleteKeyIfEmpty(a.Registry.Path); }
                                catch (Exception ex)
                                {
                                    keyNote = "Значение восстановлено, но пустой ключ не удалён: "
                                        + (UcpdGuard.IsBlockedByOs(_env.Services, a.Registry.Path, a.Registry.Name, ex) ? UcpdGuard.Reason + ". " : "") + ex.Message;
                                }
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
                // откат значения в защищённом UCPD ключе невозможен: не ошибка движка, код выхода не меняется
                var res = Blocked(t, a, ex, cur, expected, ex.Message)
                    ?? new ReportEntry(t.Id, a.Id, ActionStatus.Failed, cur, "", expected, ex.Message);
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
                    if (done.Contains((t.Id, a.Id))) be.Add(Capture(t.Id, a, backup: false));
            _store.SaveBaseline(new Baseline(FormatVersion, _runId + "-revert", _env.Now(), be, IsRevertMarker: true));
        }
        catch { /* маркер вспомогательный: сбой не должен ломать отчёт об откате */ }
    }
}
