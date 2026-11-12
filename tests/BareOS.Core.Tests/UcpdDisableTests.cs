using BareOS.Core;
using BareOS.Core.Engine;
using Xunit;
using static BareOS.Core.Tests.Rig;

namespace BareOS.Core.Tests;

/// <summary>Явное отключение защиты UCPD через UcpdGuard.DisableProtectionTweak (D-032, флаг CLI --allow-ucpd-disable,
/// не каталожная карточка). Полный путь (CLI, подстановка в список твиков) проверяется только на ВМ.</summary>
public class UcpdDisableTests
{
    [Fact]
    public void DisableProtectionTweak_HasServiceAndTaskActions_IsProposed()
    {
        var t = UcpdGuard.DisableProtectionTweak();
        Assert.Equal("proposed", t.Status);
        Assert.Equal(2, t.Actions.Count);
        var svc = t.Actions.Single(a => a.Service != null).Service!;
        Assert.Equal(UcpdGuard.ServiceName, svc.Name);
        Assert.Equal(StartType.Disabled, svc.Start);
        var task = t.Actions.Single(a => a.Task != null).Task!;
        Assert.Equal(UcpdGuard.VelocityTask, task.Path);
        Assert.False(task.Enabled);
    }

    [Fact]
    public void Apply_DisableTweakFirst_DisablesServiceAndTask_BeforeProtectedWrite()
    {
        var r = new Rig();
        r.Svc.States[UcpdGuard.ServiceName] = new ServiceState(true, StartType.Boot, true);
        r.Tasks.States[UcpdGuard.VelocityTask] = new TaskState(true, true);

        var disable = UcpdGuard.DisableProtectionTweak();
        var other = T("t", Dw("a", "A", 1));
        var rep = r.Engine.Apply(new[] { disable, other });

        Assert.True(rep.Ok);
        Assert.Equal(StartType.Disabled, r.Svc.States[UcpdGuard.ServiceName].Start);
        Assert.False(r.Svc.States[UcpdGuard.ServiceName].Running);   // apply останавливает запущенную службу
        Assert.False(r.Tasks.States[UcpdGuard.VelocityTask].Enabled);
        Assert.Equal(ActionStatus.Applied, rep.Entries.Single(e => e.ActionId == "service").Status);
        Assert.Equal(ActionStatus.Applied, rep.Entries.Single(e => e.ActionId == "task").Status);
        Assert.Equal(ActionStatus.Applied, rep.Entries.Single(e => e.ActionId == "a").Status);
    }

    [Fact]
    public void Revert_DisableTweakLast_RestoresServiceAndTask_AfterOtherValues()
    {
        var r = new Rig();
        r.Svc.States[UcpdGuard.ServiceName] = new ServiceState(true, StartType.Boot, true);
        r.Tasks.States[UcpdGuard.VelocityTask] = new TaskState(true, true);

        var disable = UcpdGuard.DisableProtectionTweak();
        var other = T("t", Dw("a", "A", 1));
        Assert.True(r.Engine.Apply(new[] { disable, other }).Ok);

        var rev = r.NextRun("run2").Revert(new[] { disable, other });
        Assert.True(rev.Ok);
        Assert.Equal(ActionStatus.Reverted, rev.Entries.Single(e => e.ActionId == "service").Status);
        Assert.Equal(ActionStatus.Reverted, rev.Entries.Single(e => e.ActionId == "task").Status);
        Assert.Equal(StartType.Boot, r.Svc.States[UcpdGuard.ServiceName].Start);
        Assert.True(r.Svc.States[UcpdGuard.ServiceName].Running);     // apply остановил, revert запустил обратно
        Assert.True(r.Tasks.States[UcpdGuard.VelocityTask].Enabled);
    }
}
