using BareOS.Core;
using BareOS.Core.Engine;
using Xunit;

namespace BareOS.Core.Tests;

public class CatalogTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wos-cat-" + Guid.NewGuid().ToString("N"));
    public CatalogTests() => Directory.CreateDirectory(dir);
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    static string Yml(string id = "privacy.test", string evidence = "event count drops",
        string path = @"HKLM\SOFTWARE\Policies\Test") => $$"""
    id: {{id}}
    title: T
    verdict: Core
    stability: Stable
    intent: i
    evidence: "{{evidence}}"
    actions:
      - id: a1
        registry: { path: '{{path}}', name: X, kind: DWord, data: "0" }
    """;

    string Write(string name, string content) { var p = Path.Combine(dir, name); File.WriteAllText(p, content); return p; }

    [Fact]
    public void ValidFile_Loads()
    {
        Write("a.yml", Yml());
        var all = TweakCatalog.LoadDirectory(dir);
        var t = Assert.Single(all);
        Assert.Equal("privacy.test", t.Id);
        Assert.Equal(RegKind.DWord, t.Actions[0].Registry!.Kind);
        Assert.Equal(Verdict.Core, t.Verdict);
    }

    [Fact]
    public void DuplicateId_Rejected()
    {
        Write("a.yml", Yml("dup.id"));
        Write("b.yml", Yml("dup.id"));
        Assert.Throws<TweakValidationException>(() => TweakCatalog.LoadDirectory(dir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyEvidence_Rejected(string ev)
    {
        Write("a.yml", Yml(evidence: ev));
        Assert.Throws<TweakValidationException>(() => TweakCatalog.LoadDirectory(dir));
        Assert.Throws<TweakValidationException>(() => TweakCatalog.LoadFile(Path.Combine(dir, "a.yml")));
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows Defender\Features")]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows Advanced Threat Protection")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\WinDefend")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Virtualization")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\vmms")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\LxssManager")]
    public void ForbiddenRegistryTargets_Rejected(string path)
    {
        Write("a.yml", Yml(path: path));
        Assert.Throws<TweakValidationException>(() => TweakCatalog.LoadDirectory(dir));
    }

    [Theory]
    [InlineData("WinDefend")]
    [InlineData("WdFilter")]
    [InlineData("Sense")]
    [InlineData("vmms")]
    [InlineData("LxssManager")]
    [InlineData("HvHost")]
    public void ForbiddenServices_Rejected(string svc)
    {
        Write("a.yml", $$"""
        id: svc.t
        title: T
        verdict: Core
        stability: Stable
        intent: i
        evidence: e
        actions:
          - id: a1
            service: { name: "{{svc}}", start: Disabled }
        """);
        Assert.Throws<TweakValidationException>(() => TweakCatalog.LoadDirectory(dir));
    }

    [Fact]
    public void ActionWithBothOrNeither_Rejected()
    {
        Write("a.yml", """
        id: x.y
        title: T
        verdict: Core
        stability: Stable
        intent: i
        evidence: e
        actions:
          - id: a1
        """);
        Assert.Throws<TweakValidationException>(() => TweakCatalog.LoadDirectory(dir));
    }

    [Fact]
    public void ShippedCatalogDirectory_AllValid()
    {
        var d = FindCatalog();
        if (d == null) return; // папки ещё нет — пропуск
        var files = Directory.GetFiles(d, "*.yml", SearchOption.AllDirectories);
        var all = TweakCatalog.LoadDirectory(d);
        Assert.Equal(files.Length, all.Count);
        foreach (var t in all)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Evidence), t.Id);
            Assert.NotEmpty(t.Actions);
        }
        foreach (var f in files) TweakCatalog.LoadFile(f);
    }

    static string? FindCatalog()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            var c = Path.Combine(d.FullName, "catalog");
            if (Directory.Exists(c) && File.Exists(Path.Combine(d.FullName, "BareOS.sln"))) return c;
            d = d.Parent;
        }
        return null;
    }
}
