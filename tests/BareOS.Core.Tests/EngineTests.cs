using BareOS.Core;
using Xunit;
using static BareOS.Core.Tests.Rig;

namespace BareOS.Core.Tests;

public class EngineTests
{
    static ReportEntry One(Report r, string action) => r.Entries.Single(e => e.ActionId == action);

    [Fact]
    public void Plan_DoesNotWrite()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        var plan = r.Engine.Plan(new[] { T("t", Dw("a", "A", 0)) });
        Assert.Single(plan.Entries);
        Assert.Empty(r.Reg.Writes);
        Assert.Empty(r.Store.Baselines);
        Assert.Equal(D(1), r.Reg.Read(K, "A"));
    }

    [Fact]
    public void Apply_SavesBaselineBeforeFirstWrite()
    {
        var store = new OrderStore();
        var r = new Rig(store);
        store.Reg = r.Reg;
        r.Reg.SetDword(K, "A", 1);
        r.Engine.Apply(new[] { T("t", Dw("a", "A", 0)) });
        Assert.NotNull(store.WritesAtBaselineTime);
        Assert.Equal(0, store.WritesAtBaselineTime);
        Assert.Single(r.Reg.Writes);
    }

    sealed class OrderStore : IRunStore
    {
        public FakeRegistry? Reg; public int? WritesAtBaselineTime; Baseline? b;
        public void SaveBaseline(Baseline x) { WritesAtBaselineTime = Reg!.Writes.Count; b = x; }
        public Baseline? LoadLatestBaseline() => b;
        public IReadOnlyList<Baseline> LoadBaselines() => b == null ? Array.Empty<Baseline>() : new[] { b };
        public void SaveReport(Report r) { }
        public void SaveJournalLine(string runId, ReportEntry e) { }
    }

    [Fact]
    public void Apply_SetsValues_AndReportsApplied()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        r.Svc.States["svc"] = new ServiceState(true, StartType.Automatic);
        var rep = r.Engine.Apply(new[] { T("t", Dw("a", "A", 0), Svc_("s", "svc", StartType.Disabled)) });
        Assert.True(rep.Ok);
        Assert.Equal(ActionStatus.Applied, One(rep, "a").Status);
        Assert.Equal(ActionStatus.Applied, One(rep, "s").Status);
        Assert.Equal(D(0), r.Reg.Read(K, "A"));
        Assert.Equal(StartType.Disabled, r.Svc.Read("svc").Start);
        Assert.Single(r.Store.Baselines);
        Assert.NotEmpty(r.Store.Journal);
        Assert.NotEmpty(r.Store.Reports);
    }

    [Fact]
    public void Apply_AlreadyApplied_NoWrite()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 0);
        var rep = r.Engine.Apply(new[] { T("t", Dw("a", "A", 0)) });
        Assert.Equal(ActionStatus.AlreadyApplied, One(rep, "a").Status);
        Assert.Empty(r.Reg.Writes);
    }

    [Fact]
    public void Apply_Failure_StopsRest_Skipped()
    {
        var r = new Rig();
        r.Reg.FailWhen = (_, n, _) => n == "B";
        var rep = r.Engine.Apply(new[] { T("t", Dw("a", "A", 0), Dw("b", "B", 0), Dw("c", "C", 0)) });
        Assert.Equal(ActionStatus.Applied, One(rep, "a").Status);
        Assert.Equal(ActionStatus.Failed, One(rep, "b").Status);
        Assert.NotNull(One(rep, "b").Error);
        Assert.Equal(ActionStatus.Skipped, One(rep, "c").Status);
        Assert.Equal(RegValue.Missing, r.Reg.Read(K, "C"));
        Assert.False(rep.Ok);
    }

    [Fact]
    public void Apply_Failure_SkipsLaterTweaks_Too()
    {
        var r = new Rig();
        r.Reg.FailWhen = (_, n, _) => n == "A";
        var rep = r.Engine.Apply(new[] { T("t1", Dw("a", "A", 0)), T("t2", Dw("b", "B", 0)) });
        Assert.Equal(ActionStatus.Failed, One(rep, "a").Status);
        Assert.Equal(ActionStatus.Skipped, One(rep, "b").Status);
        Assert.Empty(r.Reg.Writes);
    }

    [Fact]
    public void Apply_ServiceFailure_StopsRest()
    {
        var r = new Rig();
        r.Svc.States["svc"] = new ServiceState(true, StartType.Automatic);
        r.Svc.FailWhen = (_, _) => true;
        var rep = r.Engine.Apply(new[] { T("t", Svc_("s", "svc", StartType.Disabled), Dw("b", "B", 0)) });
        Assert.Equal(ActionStatus.Failed, One(rep, "s").Status);
        Assert.Equal(ActionStatus.Skipped, One(rep, "b").Status);
    }

    [Fact]
    public void Verify_Verified_And_Mismatch()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 0);
        r.Reg.SetDword(K, "B", 5);
        var rep = r.Engine.Verify(new[] { T("t", Dw("a", "A", 0), Dw("b", "B", 0)) });
        Assert.Equal(ActionStatus.Verified, One(rep, "a").Status);
        Assert.Equal(ActionStatus.Mismatch, One(rep, "b").Status);
        Assert.False(rep.Ok);
        Assert.Empty(r.Reg.Writes);
    }

    [Fact]
    public void Verify_MissingValue_IsMismatch()
    {
        var r = new Rig();
        var rep = r.Engine.Verify(new[] { T("t", Dw("a", "A", 0)) });
        Assert.Equal(ActionStatus.Mismatch, One(rep, "a").Status);
    }

    [Fact]
    public void Verify_Service()
    {
        var r = new Rig();
        r.Svc.States["svc"] = new ServiceState(true, StartType.Disabled);
        var rep = r.Engine.Verify(new[] { T("t", Svc_("s", "svc", StartType.Disabled)) });
        Assert.Equal(ActionStatus.Verified, One(rep, "s").Status);
    }

    [Fact]
    public void Revert_RestoresBaseline()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        r.Svc.States["svc"] = new ServiceState(true, StartType.Automatic);
        var tw = new[] { T("t", Dw("a", "A", 0), Svc_("s", "svc", StartType.Disabled)) };
        r.Engine.Apply(tw);
        var rep = r.Engine.Revert(tw);
        Assert.Equal(ActionStatus.Reverted, One(rep, "a").Status);
        Assert.Equal(ActionStatus.Reverted, One(rep, "s").Status);
        Assert.Equal(D(1), r.Reg.Read(K, "A"));
        Assert.Equal(StartType.Automatic, r.Svc.Read("svc").Start);
        Assert.True(rep.Ok);
    }

    [Fact]
    public void Revert_ValueThatDidNotExist_IsDeleted()
    {
        var r = new Rig();
        var tw = new[] { T("t", Dw("a", "A", 0)) };
        r.Engine.Apply(tw);
        Assert.Equal(D(0), r.Reg.Read(K, "A"));
        var rep = r.Engine.Revert(tw);
        Assert.Equal(ActionStatus.Reverted, One(rep, "a").Status);
        Assert.False(r.Reg.Read(K, "A").Exists);
        Assert.False(r.Reg.Values.ContainsKey((K.ToUpperInvariant(), "a")));
    }

    [Fact]
    public void Revert_NothingToRevert_WhenCurrentEqualsBaseline()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        var tw = new[] { T("t", Dw("a", "A", 0)) };
        r.Engine.Apply(tw);
        r.Reg.SetDword(K, "A", 1); // вернули руками к исходному
        r.Reg.Writes.Clear();
        var rep = r.Engine.Revert(tw);
        Assert.Equal(ActionStatus.NothingToRevert, One(rep, "a").Status);
        Assert.Empty(r.Reg.Writes);
    }

    [Fact]
    public void Revert_Conflict_WhenThirdValue_NoWrite()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        var tw = new[] { T("t", Dw("a", "A", 0)) };
        r.Engine.Apply(tw);
        r.Reg.SetDword(K, "A", 7); // кто-то изменил
        r.Reg.Writes.Clear();
        var rep = r.Engine.Revert(tw);
        Assert.Equal(ActionStatus.Conflict, One(rep, "a").Status);
        Assert.Empty(r.Reg.Writes);
        Assert.Equal(D(7), r.Reg.Read(K, "A"));
        Assert.False(rep.Ok);
    }

    [Fact]
    public void Revert_ServiceConflict()
    {
        var r = new Rig();
        r.Svc.States["svc"] = new ServiceState(true, StartType.Automatic);
        var tw = new[] { T("t", Svc_("s", "svc", StartType.Disabled)) };
        r.Engine.Apply(tw);
        r.Svc.States["svc"] = new ServiceState(true, StartType.Manual);
        r.Svc.Writes.Clear();
        var rep = r.Engine.Revert(tw);
        Assert.Equal(ActionStatus.Conflict, One(rep, "s").Status);
        Assert.Empty(r.Svc.Writes);
    }

    [Fact]
    public void Revert_ReverseOrder()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        r.Reg.SetDword(K, "B", 1);
        var tw = new[] { T("t", Dw("a", "A", 0), Dw("b", "B", 0)) };
        r.Engine.Apply(tw);
        r.Reg.Writes.Clear();
        r.Engine.Revert(tw);
        Assert.Equal(new[] { K + "|B", K + "|A" }, r.Reg.Writes);
    }

    [Fact]
    public void Revert_WithoutBaseline_DoesNotWrite()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 0);
        try { r.Engine.Revert(new[] { T("t", Dw("a", "A", 0)) }); } catch (Exception) { }
        Assert.Empty(r.Reg.Writes);
    }

    [Fact]
    public void Baseline_NotOverwrittenOnSecondApply()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        var tw = new[] { T("t", Dw("a", "A", 0)) };
        r.Engine.Apply(tw);
        var first = r.Store.Baselines.Single();
        try { r.Engine.Apply(tw); } catch (Exception) { /* допустимо: движок не должен сохранять второй */ }
        Assert.Single(r.Store.Baselines);
        Assert.Same(first, r.Store.LoadLatestBaseline());
        var b = first.Entries.Single(e => e.ActionId == "a");
        Assert.Equal(D(1), b.Registry);
        // откат всё ещё возвращает исходное
        r.Engine.Revert(tw);
        Assert.Equal(D(1), r.Reg.Read(K, "A"));
    }

    [Fact]
    public void Baseline_RecordsMissingValue_AndService()
    {
        var r = new Rig();
        r.Svc.States["svc"] = new ServiceState(true, StartType.Manual);
        r.Engine.Apply(new[] { T("t", Dw("a", "A", 0), Svc_("s", "svc", StartType.Disabled)) });
        var b = r.Store.Baselines.Single();
        Assert.False(b.Entries.Single(e => e.ActionId == "a").Registry!.Exists);
        Assert.Equal(StartType.Manual, b.Entries.Single(e => e.ActionId == "s").Service!.Start);
    }
}

public class TaskEngineTests
{
    const string P = @"\Microsoft\Windows\Test\Task";

    [Fact]
    public void Apply_DisablesEnabledTask_AndRevertRestores()
    {
        var r = new Rig();
        r.Tasks.States[P] = new TaskState(true, true);
        var t = Rig.T("t.task", Rig.Task_("a", P, false));
        var rep = r.Engine.Apply(new[] { t });
        Assert.Equal(ActionStatus.Applied, rep.Entries.Single().Status);
        Assert.False(r.Tasks.Read(P).Enabled);
        Assert.Equal(ActionStatus.Verified, r.Engine.Verify(new[] { t }).Entries.Single().Status);

        var rev = r.Engine.Revert(new[] { t });
        Assert.Equal(ActionStatus.Reverted, rev.Entries.Single().Status);
        Assert.True(r.Tasks.Read(P).Enabled);
    }

    [Fact]
    public void Apply_MissingTask_WhenDisabledExpected_IsAlreadyApplied_NoWrites()
    {
        var r = new Rig();
        var rep = r.Engine.Apply(new[] { Rig.T("t.task", Rig.Task_("a", P, false)) });
        Assert.Equal(ActionStatus.AlreadyApplied, rep.Entries.Single().Status);
        Assert.Empty(r.Tasks.Writes);
    }

    [Fact]
    public void Verify_AfterApply_DoesNotWrite()
    {
        var r = new Rig();
        r.Tasks.States[P] = new TaskState(true, true);
        var t = Rig.T("t.task", Rig.Task_("a", P, false));
        r.Engine.Apply(new[] { t });
        var n = r.Tasks.Writes.Count;
        // второй прогон в новом движке на том же хранилище невозможен (baseline не перезаписывается) — проверяем идемпотентность на уровне MatchesExpected
        Assert.Equal(ActionStatus.Verified, r.Engine.Verify(new[] { t }).Entries.Single().Status);
        Assert.Equal(n, r.Tasks.Writes.Count);
    }

    [Fact]
    public void Revert_Conflict_WhenTaskChangedByUser()
    {
        var r = new Rig();
        r.Tasks.States[P] = new TaskState(true, false); // baseline: Disabled
        var t = Rig.T("t.task", Rig.Task_("a", P, true)); // ожидаем Enabled
        r.Engine.Apply(new[] { t });
        Assert.True(r.Tasks.Read(P).Enabled);
        r.Tasks.States.Remove(P); // задача пропала: ни baseline, ни ожидаемое
        var rev = r.Engine.Revert(new[] { t });
        Assert.Equal(ActionStatus.Conflict, rev.Entries.Single().Status);
    }

    [Fact]
    public void Apply_WriteFailure_ReportsFailed()
    {
        var r = new Rig();
        r.Tasks.States[P] = new TaskState(true, true);
        r.Tasks.FailWhen = (_, _) => true;
        var rep = r.Engine.Apply(new[] { Rig.T("t.task", Rig.Task_("a", P, false)) });
        Assert.Equal(ActionStatus.Failed, rep.Entries.Single().Status);
        Assert.False(rep.Ok);
    }

    [Fact]
    public void Plan_ShowsTaskTarget()
    {
        var r = new Rig();
        r.Tasks.States[P] = new TaskState(true, true);
        var e = r.Engine.Plan(new[] { Rig.T("t.task", Rig.Task_("a", P, false)) }).Entries.Single();
        Assert.Equal("task:" + P, e.Target);
        Assert.Equal("Enabled", e.Before);
    }
}
