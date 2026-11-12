using BareOS.Core;
using BareOS.Core.Engine;
using BareOS.Core.Windows;
using Xunit;

namespace BareOS.Core.Tests;

public class RealCatalogTests
{
    static string CatalogDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "catalog"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "catalog");
    }

    [Fact]
    public void RepoCatalog_IsValid_AndHasAtLeast10Cards()
    {
        var all = TweakCatalog.LoadDirectory(CatalogDir());
        Assert.True(all.Count >= 10, $"карточек: {all.Count}");
        Assert.All(all, t => Assert.False(string.IsNullOrWhiteSpace(t.Evidence)));
    }

    [Fact]
    public void RepoCatalog_UcpdProtectedTargets_AreProposedUntilVerifiedMethod()
    {
        var all = TweakCatalog.LoadDirectory(CatalogDir());
        var hit = all.Where(t => t.Actions.Any(a => a.Registry != null && UcpdGuard.IsProtected(a.Registry.Path, a.Registry.Name))).ToList();
        Assert.Contains(hit, t => t.Id == "shell.widgets");
        Assert.All(hit, t =>
        {
            Assert.Equal("proposed", t.Status);
            Assert.Contains("UCPD", t.Evidence);
        });
    }

    [Fact]
    public void RepoCatalog_HasTaskCard()
        => Assert.Contains(TweakCatalog.LoadDirectory(CatalogDir()), t => t.Actions.Any(a => a.Task != null));

    [Fact]
    public void RepoCatalog_RegistryTargetsAreUnique()
    {
        var all = TweakCatalog.LoadDirectory(CatalogDir());
        var keys = all.SelectMany(t => t.Actions.Where(a => a.Registry != null).Select(a => (a.Registry!.Path + "\\" + a.Registry.Name).ToUpperInvariant()));
        Assert.Equal(keys.Count(), keys.Distinct().Count());
    }
}

public class TaskCatalogAndXmlTests
{
    static Tweak LoadYaml(string yaml)
    {
        var f = Path.Combine(Path.GetTempPath(), "wos-task-" + Guid.NewGuid().ToString("N") + ".yml");
        File.WriteAllText(f, yaml);
        try { return Assert.Single(TweakCatalog.LoadFile(f)); } finally { File.Delete(f); }
    }

    const string Head = "id: t.x\ntitle: T\nverdict: Core\nstability: Fragile\nintent: i\nevidence: e\nactions:\n  - id: a\n    task:\n";

    [Fact]
    public void Task_Parses()
    {
        var t = LoadYaml(Head + "      path: '\\Microsoft\\Windows\\X\\Y'\n      state: disabled\n");
        Assert.Equal(@"\Microsoft\Windows\X\Y", t.Actions[0].Task!.Path);
        Assert.False(t.Actions[0].Task!.Enabled);
    }

    [Fact]
    public void Task_BadState_Rejected()
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Head + "      path: '\\X\\Y'\n      state: maybe\n"));

    [Fact]
    public void Task_RelativePath_Rejected()
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Head + "      path: 'X\\Y'\n      state: disabled\n"));

    [Fact]
    public void Task_DefenderPath_Rejected()
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Head + "      path: '\\Microsoft\\Windows\\Windows Defender\\Windows Defender Scheduled Scan'\n      state: disabled\n"));

    [Fact]
    public void ParseXml_DisabledSettings_IsDisabled()
        => Assert.Equal(new TaskState(true, false), TaskAccess.ParseXml("<Task><Triggers><TimeTrigger><Enabled>true</Enabled></TimeTrigger></Triggers><Settings><Enabled>false</Enabled></Settings></Task>"));

    [Fact]
    public void ParseXml_NoEnabledElement_IsEnabled_EvenIfTriggerDisabled()
        => Assert.Equal(new TaskState(true, true), TaskAccess.ParseXml("<Task><Triggers><TimeTrigger><Enabled>false</Enabled></TimeTrigger></Triggers><Settings><StartWhenAvailable>true</StartWhenAvailable></Settings></Task>"));
}

public class AppxTests
{
    static Tweak LoadYaml(string yaml)
    {
        var f = Path.Combine(Path.GetTempPath(), "bos-appx-" + Guid.NewGuid().ToString("N") + ".yml");
        File.WriteAllText(f, yaml);
        try { return Assert.Single(TweakCatalog.LoadFile(f)); } finally { File.Delete(f); }
    }

    static string Card(string name) => "id: t.appx\ntitle: T\nverdict: Standard\nstability: Stable\nintent: i\nevidence: e\nactions:\n  - id: a\n    appx:\n      name: '" + name + "'\n";

    [Fact]
    public void Appx_Parses() => Assert.Equal("Microsoft.BingWeather", LoadYaml(Card("Microsoft.BingWeather")).Actions[0].Appx!.Name);

    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.XboxGamingOverlay")]
    [InlineData("*Xbox*")]
    [InlineData("Microsoft.SecHealthUI")]
    [InlineData("MicrosoftCorporationII.WindowsSubsystemForLinux")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.VCLibs.140.00")]
    public void Appx_ProtectedNames_Rejected(string name)
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Card(name)));

    [Theory]
    [InlineData("*")]
    [InlineData("ab")]
    [InlineData("Foo;Remove-Item")]
    [InlineData("a b c")]
    public void Appx_BadPattern_Rejected(string name)
        => Assert.Throws<TweakValidationException>(() => LoadYaml(Card(name)));

    [Fact]
    public void AppxAccess_Quote_AllowsSafeOnly()
    {
        Assert.Equal("'Microsoft.Copilot'", AppxAccess.Quote("Microsoft.Copilot"));
        Assert.Throws<ArgumentException>(() => AppxAccess.Quote("x'; calc; '"));
    }

    [Fact]
    public void AppxAccess_ParseLines_FiltersAndDedupes()
    {
        var r = AppxAccess.ParseLines("installed:A_1\r\nnoise\r\ninstalled:A_1\r\nprovisioned:A_2\r\n");
        Assert.Equal(new[] { "installed:A_1", "provisioned:A_2" }, r);
    }
}

public class AppxEngineTests
{
    [Fact]
    public void Apply_RemovesPackage_VerifyOk_RevertIsHonestSkip()
    {
        var r = new Rig();
        r.Appx.Installed.Add("Microsoft.BingWeather_4.0_x64");
        var t = Rig.T("t.appx", Rig.Appx_("a", "Microsoft.BingWeather"));
        var rep = r.Engine.Apply(new[] { t });
        Assert.Equal(ActionStatus.Applied, rep.Entries.Single().Status);
        Assert.Empty(r.Appx.Installed);
        Assert.Equal(ActionStatus.Verified, r.Engine.Verify(new[] { t }).Entries.Single().Status);

        var rev = r.Engine.Revert(new[] { t });
        var e = rev.Entries.Single();
        Assert.Equal(ActionStatus.Skipped, e.Status);
        Assert.Contains("D-033", e.Error);
        Assert.Contains("BingWeather", e.Error);   // baseline сохранил, что было установлено
        Assert.True(rev.Ok);
    }

    [Fact]
    public void Apply_NotInstalled_IsAlreadyApplied_NoRemove()
    {
        var r = new Rig();
        var rep = r.Engine.Apply(new[] { Rig.T("t.appx", Rig.Appx_("a", "Microsoft.BingNews")) });
        Assert.Equal(ActionStatus.AlreadyApplied, rep.Entries.Single().Status);
        Assert.Empty(r.Appx.Removed);
    }

    [Fact]
    public void Apply_RemoveFails_Failed()
    {
        var r = new Rig();
        r.Appx.Installed.Add("Microsoft.Copilot_1.0_x64");
        r.Appx.FailWhen = _ => true;
        var rep = r.Engine.Apply(new[] { Rig.T("t.appx", Rig.Appx_("a", "Microsoft.Copilot")) });
        Assert.Equal(ActionStatus.Failed, rep.Entries.Single().Status);
        Assert.False(rep.Ok);
    }

    [Fact]
    public void Plan_ShowsPresence()
    {
        var r = new Rig();
        r.Appx.Installed.Add("Microsoft.BingNews_1.0_x64");
        var e = r.Engine.Plan(new[] { Rig.T("t.appx", Rig.Appx_("a", "Microsoft.BingNews")) }).Entries.Single();
        Assert.Equal("appx:Microsoft.BingNews", e.Target);
        Assert.Contains("present", e.Before);
        Assert.Equal("<absent>", e.Expected);
    }

    [Fact]
    public void Verify_StillPresent_Mismatch()
    {
        var r = new Rig();
        r.Appx.Installed.Add("Microsoft.BingNews_1.0_x64");
        var e = r.Engine.Verify(new[] { Rig.T("t.appx", Rig.Appx_("a", "Microsoft.BingNews")) }).Entries.Single();
        Assert.Equal(ActionStatus.Mismatch, e.Status);
    }
}

public class ServiceMissingTests
{
    [Fact]
    public void Disabled_ForMissingService_IsAlreadyApplied_NoWrites()
    {
        var r = new Rig();
        var rep = r.Engine.Apply(new[] { Rig.T("t.svc", Rig.Svc_("a", "NoSuchService", StartType.Disabled)) });
        Assert.Equal(ActionStatus.AlreadyApplied, rep.Entries.Single().Status);
        Assert.Empty(r.Svc.Writes);
        Assert.Equal(ActionStatus.Verified, r.Engine.Verify(new[] { Rig.T("t.svc", Rig.Svc_("a", "NoSuchService", StartType.Disabled)) }).Entries.Single().Status);
    }

    [Fact]
    public void Manual_ForMissingService_StillFails()
    {
        var r = new Rig();
        r.Svc.FailWhen = (_, _) => true;
        var rep = r.Engine.Apply(new[] { Rig.T("t.svc", Rig.Svc_("a", "NoSuchService", StartType.Manual)) });
        Assert.Equal(ActionStatus.Failed, rep.Entries.Single().Status);
    }
}
