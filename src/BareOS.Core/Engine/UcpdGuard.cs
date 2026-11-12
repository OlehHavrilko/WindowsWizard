using System.Security;

namespace BareOS.Core.Engine;

/// <summary>Ключ реестра, который на Windows 11 24H2+ защищает драйвер UCPD (User Choice Protection Driver).
/// Name = null — защищён весь ключ и его подключи.</summary>
public sealed record UcpdProtectedKey(string Path, string? Name);

/// <summary>Распознавание отказа, вызванного UCPD (D-030). UCPD не отключается и не обходится: движок только
/// честно сообщает «заблокировано ОС». Отказ считается блокировкой ОС, если одновременно: ошибка — «доступ запрещён»
/// (или проверка без ошибки), ключ входит в <see cref="ProtectedKeys"/> и служба UCPD запущена
/// (если состояние службы прочитать нельзя — решает один ключ).</summary>
public static class UcpdGuard
{
    public const string ServiceName = "UCPD";

    /// <summary>Короткая причина для отчёта и CLI.</summary>
    public const string Reason = "заблокировано ОС (UCPD)";

    /// <summary>Известные защищённые UCPD ключи. Список по наблюдениям на ВМ 24H2+ и по документации сообщества;
    /// гипотеза, пополняется по фактам (запись с BlockedByOS в отчёте — повод добавить ключ сюда).</summary>
    public static readonly IReadOnlyList<UcpdProtectedKey> ProtectedKeys = new UcpdProtectedKey[]
    {
        new(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", null),
        new(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds"),
        new(@"HKLM\SOFTWARE\Microsoft\PolicyManager\default\NewsAndInterests", null),
        new(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa"),
    };

    public static bool IsProtected(string path, string name)
    {
        foreach (var k in ProtectedKeys)
        {
            bool under = path.Equals(k.Path, StringComparison.OrdinalIgnoreCase)
                || (k.Name is null && path.StartsWith(k.Path + "\\", StringComparison.OrdinalIgnoreCase));
            if (under && (k.Name is null || k.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private const int ErrorAccessDenied = 5;
    private const int HResultAccessDenied = unchecked((int)0x80070005);

    /// <summary>«Доступ запрещён» в любом из обличий, в которых его отдаёт реестр .NET.</summary>
    public static bool IsAccessDenied(Exception ex)
        => ex is UnauthorizedAccessException or SecurityException
           || ex.HResult == HResultAccessDenied
           || (ex is System.ComponentModel.Win32Exception w && w.NativeErrorCode == ErrorAccessDenied)
           || (ex.InnerException is not null && IsAccessDenied(ex.InnerException));

    /// <summary>Заблокирована ли запись в (path, name) драйвером UCPD. <paramref name="error"/> = null — отказ не наблюдался
    /// (verify: значение не совпало, и менять его система не даёт).</summary>
    public static bool IsBlockedByOs(IServiceAccess services, string path, string name, Exception? error)
    {
        if (error is not null && !IsAccessDenied(error)) return false;
        if (!IsProtected(path, name)) return false;
        try
        {
            var s = services.Read(ServiceName);
            return s.Exists && s.Running;
        }
        catch (Exception)
        {
            return true;   // служба не читается — считаем по ключу
        }
    }

    public static string Explain(string path, string name, string? detail = null)
        => $"{Reason}: драйвер защиты UCPD не даёт менять {path}\\{name}. Bare OS пробует обход через локальную"
           + $" групповую политику (D-031), саму защиту ОС не отключает (D-030)."
           + (string.IsNullOrEmpty(detail) ? "" : " " + detail);
}
