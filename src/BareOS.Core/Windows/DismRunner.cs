using System.Diagnostics;
using System.Text;

namespace BareOS.Core.Windows;

/// <summary>Запуск dism.exe с аргументами списком (без оболочки, инъекция невозможна). Общий для FeatureAccess и CleanupAccess.</summary>
internal static class DismRunner
{
    /// <summary>ERROR_SUCCESS_REBOOT_REQUIRED: операция выполнена, изменение вступит в силу после перезагрузки.</summary>
    public const int RebootRequired = 3010;

    public static (int Code, string Output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("dism.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить dism.exe");
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output + err.Result);
    }
}
