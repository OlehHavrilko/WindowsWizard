using BareOS.Core;
using BareOS.Core.Engine;
using BareOS.Core.Windows;
using Xunit;
using static BareOS.Core.Tests.Rig;

namespace BareOS.Core.Tests;

/// <summary>Регрессии аудита перед первым запуском на Windows (2026-11-11).</summary>
public class AuditRegressionTests
{
    static ReportEntry One(Report r, string action) => r.Entries.Single(e => e.ActionId == action);

    [Fact]
    public void SecondApply_DoesNotLoseOriginal_RevertRestores()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        r.Svc.States["svc"] = new ServiceState(true, StartType.Manual);
        var tw = new[] { T("t", Dw("a", "A", 0), Svc_("s", "svc", StartType.Disabled)) };
        Assert.True(r.Engine.Apply(tw).Ok);
        var again = r.NextRun("run2").Apply(tw);           // повторный apply: всё AlreadyApplied, новый baseline = ожидаемое
        Assert.All(again.Entries, e => Assert.Equal(ActionStatus.AlreadyApplied, e.Status));
        Assert.Equal(2, r.Store.Baselines.Count);

        var rev = r.NextRun("run3").Revert(tw);
        Assert.True(rev.Ok);
        Assert.Equal(ActionStatus.Reverted, One(rev, "a").Status);
        Assert.Equal(D(1), r.Reg.Read(K, "A"));
        Assert.Equal(StartType.Manual, r.Svc.Read("svc").Start);
    }

    [Fact]
    public void ApplyInParts_RevertAll_RestoresBoth()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        r.Reg.SetDword(K, "B", 1);
        var ta = T("ta", Dw("a", "A", 0));
        var tb = T("tb", Dw("b", "B", 0));
        r.Engine.Apply(new[] { ta });                     // --only ta
        r.NextRun("run2").Apply(new[] { tb });            // --only tb
        var rev = r.NextRun("run3").Revert(new[] { ta, tb });
        Assert.True(rev.Ok);
        Assert.Equal(D(1), r.Reg.Read(K, "A"));
        Assert.Equal(D(1), r.Reg.Read(K, "B"));
    }

    [Fact]
    public void ApplyRevert_UserChange_ApplyRevert_RestoresLatestOriginal()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        var tw = new[] { T("t", Dw("a", "A", 0)) };
        r.Engine.Apply(tw);
        r.NextRun("run2").Revert(tw);
        Assert.Equal(D(1), r.Reg.Read(K, "A"));
        r.Reg.SetDword(K, "A", 7);                        // пользователь поменял сам
        r.NextRun("run3").Apply(tw);
        r.NextRun("run4").Revert(tw);
        Assert.Equal(D(7), r.Reg.Read(K, "A"));
    }

    [Fact]
    public void OriginallyExpected_RevertIsNothingToRevert()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 0);
        var tw = new[] { T("t", Dw("a", "A", 0)) };
        r.Engine.Apply(tw);
        r.NextRun("run2").Apply(tw);
        var rev = r.NextRun("run3").Revert(tw);
        Assert.Equal(ActionStatus.NothingToRevert, rev.Entries.Single().Status);
        Assert.Equal(D(0), r.Reg.Read(K, "A"));
    }

    [Fact]
    public void DelayedAutostart_IsRestoredAsDelayed()
    {
        var r = new Rig();
        r.Svc.States["svc"] = new ServiceState(true, StartType.AutomaticDelayed);
        var tw = new[] { T("t", Svc_("s", "svc", StartType.Disabled)) };
        r.Engine.Apply(tw);
        r.NextRun("run2").Revert(tw);
        Assert.Equal(StartType.AutomaticDelayed, r.Svc.Read("svc").Start);
    }

    [Theory]
    [InlineData("0x1")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData(" 1")]
    [InlineData("4294967296")]
    public void Catalog_RejectsNonCanonicalDword(string data)
    {
        var t = new Tweak("x.y", "x", Verdict.Core, Stability.Stable, "i", "e",
            new[] { new TweakAction("a", Registry: new RegistryTarget(@"HKLM\SOFTWARE\Test", "A", RegKind.DWord, data)) });
        Assert.Throws<TweakValidationException>(() => TweakCatalog.Validate(t));
    }

    [Fact]
    public void Catalog_AcceptsCanonicalDword()
    {
        var t = new Tweak("x.y", "x", Verdict.Core, Stability.Stable, "i", "e",
            new[] { new TweakAction("a", Registry: new RegistryTarget(@"HKLM\SOFTWARE\Test", "A", RegKind.DWord, "4294967295")) });
        TweakCatalog.Validate(t);
    }

    [Fact]
    public void TaskXml_Garbage_Throws_NotEnabled()
        => Assert.Throws<InvalidOperationException>(() => TaskAccess.ParseXml("ERROR: Access is denied."));

    [Fact]
    public void TaskXml_Utf16Nulls_Parsed()
    {
        var xml = "<?xml version=\"1.0\"?><Task><Settings><Enabled>false</Enabled></Settings></Task>";
        var utf16ish = string.Concat(xml.Select(c => c + "\0"));
        Assert.Equal(new TaskState(true, false), TaskAccess.ParseXml(utf16ish));
    }

    [Fact]
    public void Store_LoadBaselines_Ordered_AndCorruptSkipped()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wz-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new JsonRunStore(dir);
            var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            s.SaveBaseline(new Baseline("1", "b2", t0.AddMinutes(1), Array.Empty<BaselineEntry>()));
            s.SaveBaseline(new Baseline("1", "b1", t0, Array.Empty<BaselineEntry>()));
            Assert.Equal(new[] { "b1", "b2" }, s.LoadBaselines().Select(b => b.BaselineId));
            Assert.Equal("b2", s.LoadLatestBaseline()!.BaselineId);

            Directory.CreateDirectory(Path.Combine(dir, "bad"));
            File.WriteAllText(Path.Combine(dir, "bad", "baseline.json"), "{ not json");
            Assert.Equal(2, s.LoadBaselines().Count);          // битый пропущен
            Assert.Single(s.CorruptFiles);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RevertedApply_DoesNotResurrect_OnLaterRevert()
    {
        var r = new Rig();
        r.Reg.SetDword(K, "A", 1);
        var tw = new[] { T("t", Dw("a", "A", 0)) };
        r.Engine.Apply(tw);                       // b1: A=1
        r.NextRun("run2").Revert(tw);             // A=1, маркер
        r.Reg.SetDword(K, "A", 0);                // кто-то (GPO) выставил 0
        r.NextRun("run3").Apply(tw);              // AlreadyApplied, b: A=0
        var rev = r.NextRun("run4").Revert(tw);
        Assert.Equal(D(0), r.Reg.Read(K, "A"));   // чужое значение не затёрто старым baseline
        Assert.Equal(ActionStatus.NothingToRevert, rev.Entries.Single().Status);
    }
}
