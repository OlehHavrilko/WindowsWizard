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
