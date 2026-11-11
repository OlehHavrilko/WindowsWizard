using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using WizardOS.Core;
using WizardOS.Core.Engine;
using WizardOS.Core.Windows;

namespace WizardOS.Cli;

public static class Program
{
    private const string Usage = """
        Использование: wizardos <plan|apply|verify|revert|report> [опции]
          --catalog <dir>   каталог карточек (по умолчанию ./catalog)
          --data <dir>      каталог прогонов (по умолчанию %ProgramData%\WizardOS\runs)
          --only <id,id>    выполнить только перечисленные твики
          --run <id|latest> (report) прогон для отчёта, по умолчанию latest
          --json            вывод в JSON
        Код выхода: 0 — всё в порядке, 1 — есть проблемы, 2 — ошибка использования/валидации.
        """;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (TweakValidationException e) { return Fail($"Ошибка валидации: {e.Message}"); }
        catch (PlatformNotSupportedException e) { return Fail(e.Message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { return Fail($"Ошибка: {e.Message}"); }
    }

    private static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 2;
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Error.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        var command = args[0].ToLowerInvariant();
        if (command is not ("plan" or "apply" or "verify" or "revert"))
            return Fail($"Неизвестная команда: {args[0]}\n{Usage}");

        string tweaksDir = "catalog";
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WizardOS", "runs");
        HashSet<string>? only = null;
        bool json = false;
        string runArg = "latest";

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--catalog": if (!TryNext(args, ref i, out tweaksDir!)) return Fail("--catalog требует значение"); break;
                case "--data": if (!TryNext(args, ref i, out dataDir!)) return Fail("--data требует значение"); break;
                case "--only":
                    if (!TryNext(args, ref i, out var o)) return Fail("--only требует значение");
                    only = o.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
                    break;
                case "--run": if (!TryNext(args, ref i, out runArg!)) return Fail("--run требует значение"); break;
                case "--json": json = true; break;
                default: return Fail($"Неизвестный аргумент: {args[i]}\n{Usage}");
            }
        }

        if (command == "report")
        {
            var rs = new JsonRunStore(dataDir);
            var rep = runArg == "latest" ? rs.LoadLatestReport() : rs.LoadReport(runArg);
            if (rep is null) return Fail($"Сохранённый отчёт не найден ({runArg}) в {dataDir}");
            if (json) Console.WriteLine(JsonSerializer.Serialize(rep, JsonOpts));
            else PrintReport(rep);
            return rep.Ok ? 0 : 1;
        }

        if (!Directory.Exists(tweaksDir)) return Fail($"Каталог твиков не найден: {tweaksDir}");
        IReadOnlyList<Tweak> tweaks = TweakCatalog.LoadDirectory(tweaksDir);
        if (only is not null)
        {
            var unknown = only.Where(id => tweaks.All(t => t.Id != id)).ToList();
            if (unknown.Count > 0) return Fail($"Неизвестные id твиков: {string.Join(", ", unknown)}");
            tweaks = tweaks.Where(t => only.Contains(t.Id)).ToList();
        }
        if (tweaks.Count == 0) return Fail("Нет твиков для выполнения.");

        if (command is "apply" or "revert" && !IsAdministrator())
            return Fail("Команда требует прав администратора (запустите от имени администратора).");

        if (command is "apply" or "revert")
        {
            if (!RunLock.TryAcquire(dataDir, out var lk, out var holder))
                return Fail($"Другой прогон уже выполняется (замок {Path.Combine(dataDir, RunLock.FileName)}: {holder}).");
            using (lk) return Execute(command, tweaks, dataDir, json);
        }
        return Execute(command, tweaks, dataDir, json);
    }

    private static int Execute(string command, IReadOnlyList<Tweak> tweaks, string dataDir, bool json)
    {
        var env = new EngineEnv(new RegistryAccess(), new ServiceAccess(), () => DateTimeOffset.Now);
        var runId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var store = new JsonRunStore(dataDir);
        ITweakEngine engine = new TweakEngine(env, store, runId);

        if (command == "plan")
        {
            var plan = engine.Plan(tweaks);
            if (json) Console.WriteLine(JsonSerializer.Serialize(plan, JsonOpts));
            else PrintPlan(plan);
            return 0;
        }

        var report = command switch
        {
            "apply" => engine.Apply(tweaks),
            "verify" => engine.Verify(tweaks),
            _ => engine.Revert(tweaks)
        };
        if (json) Console.WriteLine(JsonSerializer.Serialize(report, JsonOpts));
        else PrintReport(report);
        return report.Ok ? 0 : 1;
    }

    private static bool TryNext(string[] args, ref int i, out string value)
    {
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) { value = args[++i]; return true; }
        value = "";
        return false;
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return true; // на не-Windows реестр/службы всё равно не поддержаны
        return IsAdminWindows();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsAdminWindows()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void PrintPlan(Plan plan)
    {
        Console.WriteLine($"План {plan.RunId} ({plan.Entries.Count} действий)");
        PrintTable(new[] { "Твик", "Действие", "Цель", "До", "Ожидалось" },
            plan.Entries.Select(e => new[] { e.TweakId, e.ActionId, e.Target, e.Before, e.Expected }));
    }

    private static void PrintReport(Report r)
    {
        Console.WriteLine($"Отчёт {r.RunId}: {r.Command}, {(r.Ok ? "OK" : "ЕСТЬ ПРОБЛЕМЫ")}");
        PrintTable(new[] { "Статус", "Твик", "До", "После", "Ожидалось" },
            r.Entries.Select(e => new[] { e.Status.ToString(), $"{e.TweakId}/{e.ActionId}", e.Before, e.After, e.Expected }));
        foreach (var e in r.Entries.Where(e => e.Error is not null))
            Console.WriteLine($"  ! {e.TweakId}/{e.ActionId}: {e.Error}");
    }

    private static void PrintTable(string[] headers, IEnumerable<string[]> rows)
    {
        var all = rows.ToList();
        var w = headers.Select((h, i) => Math.Max(h.Length, all.Select(r => (r[i] ?? "").Length).DefaultIfEmpty(0).Max())).ToArray();
        string Line(string[] c) => string.Join("  ", c.Select((x, i) => (x ?? "").PadRight(w[i]))).TrimEnd();
        Console.WriteLine(Line(headers));
        Console.WriteLine(new string('-', w.Sum() + 2 * (w.Length - 1)));
        foreach (var r in all) Console.WriteLine(Line(r));
    }
}
