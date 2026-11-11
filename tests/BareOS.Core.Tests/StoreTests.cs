using BareOS.Core;
using BareOS.Core.Engine;
using Xunit;

namespace BareOS.Core.Tests;

public class StoreTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wos-store-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    static Baseline B(int v) => new("1", "b1", DateTimeOffset.UnixEpoch, new[]
    {
        new BaselineEntry("t", "a", new RegValue(true, RegKind.DWord, v.ToString()), null)
    });

    [Fact]
    public void Baseline_RoundTrips_AndIsNotOverwritten()
    {
        var s = new JsonRunStore(dir);
        s.SaveBaseline(B(1));
        var l = s.LoadLatestBaseline();
        Assert.NotNull(l);
        Assert.Equal("1", l!.Entries.Single().Registry!.Data);
        Assert.ThrowsAny<Exception>(() => s.SaveBaseline(B(2)));
        Assert.Equal("1", s.LoadLatestBaseline()!.Entries.Single().Registry!.Data);
    }

    [Fact]
    public void Empty_NoBaseline()
    {
        Assert.Null(new JsonRunStore(dir).LoadLatestBaseline());
    }

    [Fact]
    public void Report_AndJournal_Written()
    {
        var s = new JsonRunStore(dir);
        var e = new ReportEntry("t", "a", ActionStatus.Applied, "1", "0", "0");
        s.SaveJournalLine("run1", e);
        s.SaveJournalLine("run1", e);
        s.SaveReport(new Report("1", "run1", "apply", DateTimeOffset.UnixEpoch, new[] { e }));
        Assert.True(Directory.Exists(dir));
        Assert.NotEmpty(Directory.GetFiles(dir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Engine_WithJsonStore_ApplyRevert_EndToEnd()
    {
        var r = new Rig(new JsonRunStore(dir));
        r.Reg.SetDword(Rig.K, "A", 1);
        var tw = new[] { Rig.T("t", Rig.Dw("a", "A", 0)) };
        Assert.True(r.Engine.Apply(tw).Ok);
        Assert.True(r.Engine.Revert(tw).Ok);
        Assert.Equal(Rig.D(1), r.Reg.Read(Rig.K, "A"));
    }
}
