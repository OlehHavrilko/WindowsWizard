using WizardOS.Core;
using WizardOS.Core.Engine;
using Xunit;

namespace WizardOS.Core.Tests;

public class RunLockTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "wz-lock-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SecondAcquire_Fails_ThenSucceedsAfterRelease()
    {
        var d = Tmp();
        try
        {
            Assert.True(RunLock.TryAcquire(d, out var a, out _));
            Assert.False(RunLock.TryAcquire(d, out var b, out var holder));
            Assert.Null(b);
            Assert.NotNull(holder);
            a!.Dispose();
            Assert.True(RunLock.TryAcquire(d, out var c, out _));
            c!.Dispose();
        }
        finally { try { Directory.Delete(d, true); } catch { } }
    }

    [Fact]
    public void StaleLockFile_IsTakenOver()
    {
        var d = Tmp();
        try
        {
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, RunLock.FileName), "pid=999999 time=2000-01-01\n");
            Assert.True(RunLock.TryAcquire(d, out var lk, out _));
            Assert.True(lk is not null);
            lk!.Dispose();
        }
        finally { try { Directory.Delete(d, true); } catch { } }
    }
}

public class ReportStoreTests
{
    [Fact]
    public void LoadReport_AndLatest()
    {
        var d = Path.Combine(Path.GetTempPath(), "wz-rep-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new JsonRunStore(d);
            Assert.Null(s.LoadLatestReport());
            var t = DateTimeOffset.Now;
            s.SaveReport(new Report("1", "r1", "apply", t.AddMinutes(-5), new List<ReportEntry>()));
            s.SaveReport(new Report("1", "r2", "verify", t, new List<ReportEntry>()));
            Assert.Equal("r1", s.LoadReport("r1")!.RunId);
            Assert.Null(s.LoadReport("nope"));
            Assert.Equal("r2", s.LoadLatestReport()!.RunId);
        }
        finally { try { Directory.Delete(d, true); } catch { } }
    }
}
