using BareOS.Core;
using BareOS.Core.Engine;
using Xunit;
using static BareOS.Core.Tests.Rig;

namespace BareOS.Core.Tests;

/// <summary>Блокировка UCPD (D-030): отказ ОС в защищённом ключе — BlockedByOS, а не сбой движка.</summary>
public class UcpdTests
{
    const string Dsh = @"HKLM\SOFTWARE\Policies\Microsoft\Dsh";

    static Rig WithUcpd(bool running = true)
    {
        var r = new Rig();
        r.Svc.States[UcpdGuard.ServiceName] = new ServiceState(true, StartType.Boot, running);
        return r;
    }

    [Fact]
    public void Apply_ProtectedKey_Denied_IsBlockedByOS_ExitOk_OthersStillApplied()
    {
        var r = WithUcpd();
        r.Reg.DenyWriteKeys.Add(Dsh);
        var tw = new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh), Dw("b", "B", 0)) };
        var rep = r.Engine.Apply(tw);
        Assert.True(rep.Ok);
        var e = rep.Entries.Single(x => x.ActionId == "news");
        Assert.Equal(ActionStatus.BlockedByOS, e.Status);
        Assert.Contains("заблокировано ОС (UCPD)", e.Error);
        Assert.Equal(ActionStatus.Applied, rep.Entries.Single(x => x.ActionId == "b").Status);
    }

    [Fact]
    public void Apply_ProtectedKey_UcpdNotRunning_StaysFailed()
    {
        var r = WithUcpd(running: false);
        r.Reg.DenyWriteKeys.Add(Dsh);
        var rep = r.Engine.Apply(new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) });
        Assert.False(rep.Ok);
        Assert.Equal(ActionStatus.Failed, rep.Entries.Single().Status);
    }

    [Fact]
    public void Apply_ProtectedKey_ServiceUnreadable_DecidedByKey()
    {
        var r = new Rig();
        r.Svc.ThrowOnRead = true;
        r.Reg.DenyWriteKeys.Add(Dsh);
        var rep = r.Engine.Apply(new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) });
        Assert.Equal(ActionStatus.BlockedByOS, rep.Entries.Single().Status);
        Assert.True(rep.Ok);
    }

    [Fact]
    public void Apply_NotProtectedKey_Denied_IsFailed_EvenIfUcpdRuns()
    {
        var r = WithUcpd();
        r.Reg.DenyWriteKeys.Add(K);
        var rep = r.Engine.Apply(new[] { T("t", Dw("a", "A", 0)) });
        Assert.False(rep.Ok);
        Assert.Equal(ActionStatus.Failed, rep.Entries.Single().Status);
    }

    [Fact]
    public void Apply_ProtectedKey_NonAccessError_IsFailed()
    {
        var r = WithUcpd();
        r.Reg.FailWhen = (_, _, _) => true;   // IOException, не «доступ запрещён»
        var rep = r.Engine.Apply(new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) });
        Assert.Equal(ActionStatus.Failed, rep.Entries.Single().Status);
    }

    [Fact]
    public void Revert_ValueWriteBlocked_IsBlockedByOS_ExitOk()
    {
        var r = WithUcpd();
        r.Reg.Set(Dsh, "AllowNewsAndInterests", D(1));
        var tw = new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) };
        Assert.True(r.Engine.Apply(tw).Ok);
        r.Reg.DenyWriteKeys.Add(Dsh);                     // после перезагрузки UCPD закрыл ключ
        var rev = r.NextRun("run2").Revert(tw);
        Assert.True(rev.Ok);
        Assert.Equal(ActionStatus.BlockedByOS, rev.Entries.Single().Status);
    }

    [Fact]
    public void Revert_EmptyKeyNotDeletedByUcpd_IsRevertedWithWarning()
    {
        var r = WithUcpd();
        var tw = new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) };
        Assert.True(r.Engine.Apply(tw).Ok);               // ключа не было: apply его создал
        r.Reg.DenyDeleteKeys.Add(Dsh);
        var rev = r.NextRun("run2").Revert(tw);
        var e = rev.Entries.Single();
        Assert.True(rev.Ok);
        Assert.Equal(ActionStatus.Reverted, e.Status);
        Assert.Contains("заблокировано ОС (UCPD)", e.Error);
        Assert.Contains("не удалён", e.Error);
        Assert.False(r.Reg.Read(Dsh, "AllowNewsAndInterests").Exists);
    }

    [Fact]
    public void Verify_ProtectedKeyMismatch_IsBlockedNotMismatch()
    {
        var r = WithUcpd();
        var rep = r.Engine.Verify(new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) });
        Assert.True(rep.Ok);
        var e = rep.Entries.Single();
        Assert.Equal(ActionStatus.BlockedByOS, e.Status);
        Assert.Contains("n/a", e.Error);
    }

    [Fact]
    public void Verify_ProtectedKeyMatches_StaysVerified()
    {
        var r = WithUcpd();
        r.Reg.SetDword(Dsh, "AllowNewsAndInterests", 0);
        var rep = r.Engine.Verify(new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) });
        Assert.Equal(ActionStatus.Verified, rep.Entries.Single().Status);
    }

    [Fact]
    public void Verify_NotProtectedMismatch_StaysMismatch()
    {
        var r = WithUcpd();
        var rep = r.Engine.Verify(new[] { T("t", Dw("a", "A", 0)) });
        Assert.False(rep.Ok);
        Assert.Equal(ActionStatus.Mismatch, rep.Entries.Single().Status);
    }

    [Fact]
    public void Apply_ProtectedKey_PolicyFallback_AppliesViaGpo()
    {
        var r = WithUcpd().WithPolicy();
        r.Reg.DenyWriteKeys.Add(Dsh);
        var tw = new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) };
        var rep = r.Engine.Apply(tw);
        Assert.True(rep.Ok);
        var e = rep.Entries.Single();
        Assert.Equal(ActionStatus.Applied, e.Status);
        Assert.Contains("групповую политику", e.Error);
        Assert.Equal(D(0), r.Reg.Read(Dsh, "AllowNewsAndInterests"));
        Assert.Single(r.Policy!.Calls);
    }

    [Fact]
    public void Apply_ProtectedKey_PolicyFallbackFails_StaysBlockedByOS()
    {
        var r = WithUcpd().WithPolicy();
        r.Policy!.Succeeds = false;
        r.Reg.DenyWriteKeys.Add(Dsh);
        var tw = new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) };
        var rep = r.Engine.Apply(tw);
        Assert.True(rep.Ok);
        Assert.Equal(ActionStatus.BlockedByOS, rep.Entries.Single().Status);
        Assert.Single(r.Policy.Calls);
    }

    [Fact]
    public void Apply_PolicyFallback_NotTriedForUnprotectedKey()
    {
        var r = WithUcpd().WithPolicy();
        r.Reg.DenyWriteKeys.Add(K);
        var rep = r.Engine.Apply(new[] { T("t", Dw("a", "A", 0)) });
        Assert.False(rep.Ok);
        Assert.Equal(ActionStatus.Failed, rep.Entries.Single().Status);
        Assert.Empty(r.Policy!.Calls);
    }

    [Fact]
    public void Revert_ValueWriteBlocked_PolicyFallback_RevertsViaGpo()
    {
        var r = WithUcpd();
        r.Reg.Set(Dsh, "AllowNewsAndInterests", D(1));
        var tw = new[] { T("t", Dw("news", "AllowNewsAndInterests", 0, Dsh)) };
        Assert.True(r.Engine.Apply(tw).Ok);
        r.WithPolicy();
        r.Reg.DenyWriteKeys.Add(Dsh);                     // после перезагрузки UCPD закрыл ключ
        var rev = r.NextRun("run2").Revert(tw);
        Assert.True(rev.Ok);
        var e = rev.Entries.Single();
        Assert.Equal(ActionStatus.Reverted, e.Status);
        Assert.Equal(D(1), r.Reg.Read(Dsh, "AllowNewsAndInterests"));
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", true)]
    [InlineData(@"hklm\software\policies\microsoft\dsh\Sub", "X", true)]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\DshX", "X", false)]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", true)]
    [InlineData(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "Other", false)]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", true)]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Start_TrackDocs", false)]
    [InlineData(@"HKLM\SOFTWARE\Test", "A", false)]
    public void IsProtected_MatchesKnownList(string path, string name, bool expected)
        => Assert.Equal(expected, UcpdGuard.IsProtected(path, name));
}
