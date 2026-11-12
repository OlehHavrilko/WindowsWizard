using BareOS.Core;
using BareOS.Core.Engine;
using BareOS.Core.Windows;
using Xunit;

namespace BareOS.Core.Tests;

public class FeatureCatalogTests
{
    static Tweak LoadYaml(string yaml)
    {
        var f = Path.Combine(Path.GetTempPath(), "bos-feat-" + Guid.NewGuid().ToString("N") + ".yml");
        File.WriteAllText(f, yaml);
        try { return Assert.Single(TweakCatalog.LoadFile(f)); } finally { File.Delete(f); }
    }

    static string Head => "id: t.f\ntitle: T\nverdict: Standard\nstability: Stable\nintent: i\nevidence: e\nactions:\n  - id: a\n";
    static string Feat(string name, string state = "Disabled") => Head + $"    feature:\n      name: '{name}'\n      state: {state}\n";
    static string Clean(string kind) => Head + $"    cleanup:\n      kind: {kind}\n";

    [Fact]
    public void Feature_Parses()
    {
        var a = LoadYaml(Feat("Recall")).Actions[0];
        Assert.Equal(new FeatureTarget("Recall", false), a.Feature);
        Assert.Equal(new FeatureTarget("Recall", true), LoadYaml(Feat("Recall", "Enabled")).Actions[0].Feature);
    }

    [Theory]
    [InlineData("Microsoft-Hyper-V-All")]
    [InlineData("Microsoft-Hyper-V")]
    [InlineData("HypervisorPlatform")]
    [InlineData("VirtualMachinePlatform")]
    [InlineData("Microsoft-Windows-Subsystem-Linux")]
    [InlineData("Containers")]
    [InlineData("Containers-DisposableClientVM")]
    [InlineData("Windows-Defender-Default-Definitions")]
    [InlineData("windows-defender-applicationguard")]
    public void Feature_Forbidden_Rejected(string name)
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Feat(name)));

    [Theory]
    [InlineData("ab")]
    [InlineData("Recall; calc")]
    [InlineData("Recall /All")]
    [InlineData("Rec all")]
    [InlineData("Recall\"")]
    public void Feature_BadName_Rejected(string name)
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Feat(name)));

    [Fact]
    public void Feature_BadState_Rejected()
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Feat("Recall", "Maybe")));

    [Fact]
    public void Feature_AndCleanup_Together_Rejected()
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Head + "    feature:\n      name: Recall\n      state: Disabled\n    cleanup:\n      kind: Temp\n"));

    [Theory]
    [InlineData("ComponentStore", CleanupKind.ComponentStore)]
    [InlineData("Temp", CleanupKind.Temp)]
    public void Cleanup_Parses(string kind, CleanupKind expected)
        => Assert.Equal(new CleanupTarget(expected), LoadYaml(Clean(kind)).Actions[0].Cleanup);

    [Fact]
    public void Cleanup_UnknownKind_Rejected()
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Clean("ResetBase")));

    [Fact]
    public void FeatureAccess_Validate_AllowsSafeOnly()
    {
        Assert.Equal("Recall", FeatureAccess.Validate("Recall"));
        Assert.Throws<ArgumentException>(() => FeatureAccess.Validate("x /Remove & calc"));
        Assert.Throws<ArgumentException>(() => FeatureAccess.Validate("x\r\ny"));
    }

    [Theory]
    [InlineData("Feature Name : Recall\r\nState : Enabled\r\n", true, true)]
    [InlineData("State : Enable Pending\r\n", true, true)]
    [InlineData("State : Disabled\r\n", true, false)]
    [InlineData("State : Disable Pending\r\n", true, false)]
    [InlineData("State : Disabled with Payload Removed\r\n", true, false)]
    public void FeatureAccess_Parse_States(string output, bool exists, bool enabled)
        => Assert.Equal(new FeatureState(exists, enabled), FeatureAccess.Parse(0, output));

    [Fact]
    public void FeatureAccess_Parse_Unknown_IsAbsent()
        => Assert.Equal(new FeatureState(false), FeatureAccess.Parse(-2146498548, "Error: 0x800f080c\r\nFeature name Foo is unknown.\r\n"));

    [Fact]
    public void FeatureAccess_Parse_OtherError_Throws()
        => Assert.Throws<InvalidOperationException>(() => FeatureAccess.Parse(740, "Error: 740 elevated"));

    [Fact]
    public void FeatureAccess_NotWindows_Throws()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Throws<PlatformNotSupportedException>(() => new FeatureAccess().Read("Recall"));
        Assert.Throws<PlatformNotSupportedException>(() => new CleanupAccess().Temp());
    }
}

public class FeatureEngineTests
{
    static Tweak Disable(string name = "Recall") => Rig.T("t.feat", Rig.Feature_("a", name, false));

    [Fact]
    public void Apply_Disables_AndRevert_Restores()
    {
        var r = new Rig();
        r.Features.State["Recall"] = true;
        var ap = r.Engine.Apply(new[] { Disable() });
        Assert.Equal(ActionStatus.Applied, ap.Entries.Single().Status);
        Assert.Equal("Enabled", ap.Entries.Single().Before);
        Assert.Equal("Disabled", ap.Entries.Single().After);
        Assert.False(r.Features.State["Recall"]);
        Assert.Equal(ActionStatus.Verified, r.Engine.Verify(new[] { Disable() }).Entries.Single().Status);

        var rv = r.NextRun("run2").Revert(new[] { Disable() });
        Assert.Equal(ActionStatus.Reverted, rv.Entries.Single().Status);
        Assert.True(r.Features.State["Recall"]);
    }

    [Fact]
    public void Apply_AlreadyDisabled_NothingToRevert_StaysDisabled()
    {
        var r = new Rig();
        r.Features.State["Recall"] = false;
        Assert.Equal(ActionStatus.AlreadyApplied, r.Engine.Apply(new[] { Disable() }).Entries.Single().Status);
        Assert.Empty(r.Features.Calls);
        Assert.Equal(ActionStatus.NothingToRevert, r.NextRun("run2").Revert(new[] { Disable() }).Entries.Single().Status);
        Assert.False(r.Features.State["Recall"]);
    }

    [Fact]
    public void Absent_Feature_IsAppliedAndVerified()
    {
        var r = new Rig();
        var ap = r.Engine.Apply(new[] { Disable() });
        Assert.Equal(ActionStatus.AlreadyApplied, ap.Entries.Single().Status);
        Assert.True(ap.Ok);
        var v = r.Engine.Verify(new[] { Disable() }).Entries.Single();
        Assert.Equal(ActionStatus.Verified, v.Status);
        Assert.Equal("<absent>", v.After);
        Assert.Equal(ActionStatus.NothingToRevert, r.NextRun("run2").Revert(new[] { Disable() }).Entries.Single().Status);
    }

    [Fact]
    public void Enable_Absent_Feature_Fails()
    {
        var r = new Rig();
        var ap = r.Engine.Apply(new[] { Rig.T("t.feat", Rig.Feature_("a", "Recall", true)) });
        Assert.Equal(ActionStatus.Failed, ap.Entries.Single().Status);
    }

    [Fact]
    public void Apply_Failure_IsReported()
    {
        var r = new Rig();
        r.Features.State["Recall"] = true;
        r.Features.FailWhen = _ => true;
        var ap = r.Engine.Apply(new[] { Disable() });
        Assert.Equal(ActionStatus.Failed, ap.Entries.Single().Status);
        Assert.False(ap.Ok);
    }

    [Fact]
    public void Revert_Conflict_WhenChangedByThirdParty()
    {
        var r = new Rig();
        r.Features.State["Recall"] = true;
        r.Engine.Apply(new[] { Rig.T("t.feat", Rig.Feature_("a", "Recall", false)) });
        // сторонняя правка: теперь «включён» и baseline тоже «включён» => откатывать нечего
        r.Features.State["Recall"] = true;
        Assert.Equal(ActionStatus.NothingToRevert, r.NextRun("run2").Revert(new[] { Disable() }).Entries.Single().Status);
    }

    [Fact]
    public void Plan_ShowsTargetAndStates()
    {
        var r = new Rig();
        r.Features.State["Recall"] = true;
        var e = r.Engine.Plan(new[] { Disable() }).Entries.Single();
        Assert.Equal("feature:Recall", e.Target);
        Assert.Equal("Enabled", e.Before);
        Assert.Equal("Disabled (или <absent>)", e.Expected);
    }
}

public class CleanupEngineTests
{
    static Tweak Clean(CleanupKind k) => Rig.T("t.clean", Rig.Cleanup_("a", k));

    [Theory]
    [InlineData(CleanupKind.ComponentStore, "component-store")]
    [InlineData(CleanupKind.Temp, "temp")]
    public void Apply_Runs_AndReportsFreeSpace(CleanupKind kind, string call)
    {
        var r = new Rig();
        var e = r.Engine.Apply(new[] { Clean(kind) }).Entries.Single();
        Assert.Equal(ActionStatus.Applied, e.Status);
        Assert.Equal("free=1000000", e.Before);
        Assert.Equal("free=1000500", e.After);
        Assert.Equal(new[] { call }, r.Cleanup.Calls);
    }

    [Fact]
    public void Apply_Twice_RunsTwice()
    {
        var r = new Rig();
        r.Engine.Apply(new[] { Clean(CleanupKind.Temp) });
        var e = r.NextRun("run2").Apply(new[] { Clean(CleanupKind.Temp) }).Entries.Single();
        Assert.Equal(ActionStatus.Applied, e.Status);
        Assert.Equal(2, r.Cleanup.Calls.Count);
    }

    [Fact]
    public void Verify_IsAlwaysVerified()
    {
        var r = new Rig();
        Assert.Equal(ActionStatus.Verified, r.Engine.Verify(new[] { Clean(CleanupKind.ComponentStore) }).Entries.Single().Status);
        Assert.Empty(r.Cleanup.Calls);
    }

    [Fact]
    public void Revert_DoesNothing_Skipped()
    {
        var r = new Rig();
        r.Engine.Apply(new[] { Clean(CleanupKind.Temp) });
        var before = r.Cleanup.Free;
        var rv = r.NextRun("run2").Revert(new[] { Clean(CleanupKind.Temp) });
        var e = rv.Entries.Single();
        Assert.Equal(ActionStatus.Skipped, e.Status);
        Assert.Contains("необратима", e.Error);
        Assert.True(rv.Ok);
        Assert.Equal(before, r.Cleanup.Free);
        Assert.Single(r.Cleanup.Calls);
    }

    [Fact]
    public void Apply_Failure_IsReported()
    {
        var r = new Rig();
        r.Cleanup.Fail = true;
        var e = r.Engine.Apply(new[] { Clean(CleanupKind.ComponentStore) }).Entries.Single();
        Assert.Equal(ActionStatus.Failed, e.Status);
    }

    [Fact]
    public void Plan_ShowsFreeSpace()
    {
        var r = new Rig();
        var e = r.Engine.Plan(new[] { Clean(CleanupKind.Temp) }).Entries.Single();
        Assert.Equal("cleanup:Temp", e.Target);
        Assert.Equal("free=1000000", e.Before);
    }

    [Fact]
    public void CleanupAccess_IsSafeRoot_RejectsDriveRootAndShallow()
    {
        Assert.False(CleanupAccess.IsSafeRoot("/"));
        var notTemp = Directory.CreateTempSubdirectory("bos-").FullName;
        var inner = Directory.CreateDirectory(Path.Join(notTemp, "data")).FullName;
        var temp = Directory.CreateDirectory(Path.Join(notTemp, "Temp")).FullName;
        try
        {
            Assert.False(CleanupAccess.IsSafeRoot(inner));   // существует, но без каталога Temp в пути
            Assert.True(CleanupAccess.IsSafeRoot(temp));
        }
        finally { Directory.Delete(notTemp, true); }
        Assert.False(CleanupAccess.IsSafeRoot(Path.Combine(Path.GetTempPath(), "bos-definitely-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
