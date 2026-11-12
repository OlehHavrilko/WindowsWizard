using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using BareOS.Core;
using BareOS.Core.Engine;
using BareOS.Core.Windows;

namespace BareOS.Cli;

public static class Program
{
    private const string Usage = """
        Использование: bareos <plan|apply|verify|revert|report|validate|measure|proof> [опции]
          --catalog <dir>   каталог карточек (по умолчанию ./catalog)
          --data <dir>      каталог прогонов (по умолчанию %ProgramData%\BareOS\runs)
          --only <id,id>    выполнить только перечисленные твики (в т. ч. proposed; без --only берутся accepted/verified)
          --run <id|latest> (report) прогон для отчёта, по умолчанию latest
          --resources <dir> каталог ресурсов для действий file (по умолчанию ресурсы рядом с каталогом: <catalog>/../resources)
          --json            вывод в JSON
          --allow-ucpd-disable  (apply/revert) отключить саму защиту UCPD (служба + задача velocity), а не
                            обходить её (D-032); требует перезагрузки, чтобы вступить в силу; по умолчанию выключен
        measure --label <имя> --out <файл.json> — замер системы (Proof Report); без --out пишет в %ProgramData%\BareOS\proof\<label>.json.
        proof --before <a.json> --after <b.json> [--html <файл>] — сравнить два замера (по умолчанию before.json, after.json, proof.html в той же папке).
        validate — проверить каталог карточек и выйти (CI).
        Код выхода: 0 — всё в порядке (в т. ч. BlockedByOS — заблокировано ОС (UCPD)), 1 — есть проблемы, 2 — ошибка использования, валидации или среды (нет прав, замок занят).
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
        catch (Exception e) { return Fail($"Внутренняя ошибка: {e}"); }
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
        if (command is not ("plan" or "apply" or "verify" or "revert" or "report" or "validate" or "measure" or "proof"))
            return Fail($"Неизвестная команда: {args[0]}\n{Usage}");

        string tweaksDir = "catalog";
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BareOS", "runs");
        HashSet<string>? only = null;
        bool json = false;
        bool allowUcpdDisable = false;
        string runArg = "latest";
        string resourcesDir = "", label = "замер", outFile = "", beforeFile = "", afterFile = "", htmlFile = "";

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
                case "--resources": if (!TryNext(args, ref i, out resourcesDir!)) return Fail("--resources требует значение"); break;
                case "--label": if (!TryNext(args, ref i, out label!)) return Fail("--label требует значение"); break;
                case "--out": if (!TryNext(args, ref i, out outFile!)) return Fail("--out требует значение"); break;
                case "--before": if (!TryNext(args, ref i, out beforeFile!)) return Fail("--before требует значение"); break;
                case "--after": if (!TryNext(args, ref i, out afterFile!)) return Fail("--after требует значение"); break;
                case "--html": if (!TryNext(args, ref i, out htmlFile!)) return Fail("--html требует значение"); break;
                case "--allow-ucpd-disable": allowUcpdDisable = true; break;
                default: return Fail($"Неизвестный аргумент: {args[i]}\n{Usage}");
            }
        }

        if (command == "measure")
        {
            if (outFile == "") outFile = Path.Combine(ProofDir(), label + ".json");
            var snap = SystemProbe.Take(label);
            var text = BareOS.Core.Proof.Proof.Serialize(snap);
            if (outFile != "") { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outFile))!); File.WriteAllText(outFile, text); Console.WriteLine($"Замер '{label}' сохранён: {outFile} ({snap.Metrics.Count} метрик)"); }
            else Console.WriteLine(text);
            return 0;
        }
        if (command == "proof")
        {
            if (beforeFile == "") beforeFile = Path.Combine(ProofDir(), "before.json");
            if (afterFile == "") afterFile = Path.Combine(ProofDir(), "after.json");
            if (htmlFile == "") htmlFile = Path.Combine(ProofDir(), "proof.html");
            var b = BareOS.Core.Proof.Proof.Deserialize(File.ReadAllText(beforeFile));
            var a = BareOS.Core.Proof.Proof.Deserialize(File.ReadAllText(afterFile));
            Console.WriteLine(BareOS.Core.Proof.Proof.ToText(b, a));
            if (htmlFile != "") { File.WriteAllText(htmlFile, BareOS.Core.Proof.Proof.ToHtml(b, a)); Console.WriteLine($"HTML-отчёт: {htmlFile}"); }
            return 0;
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
        if (command == "validate")
        {
            Console.WriteLine($"OK: карточек {tweaks.Count}, действий {tweaks.Sum(t => t.Actions.Count)}");
            return 0;
        }
        if (only is not null)
        {
            var unknown = only.Where(id => tweaks.All(t => t.Id != id)).ToList();
            if (unknown.Count > 0) return Fail($"Неизвестные id твиков: {string.Join(", ", unknown)}");
            tweaks = tweaks.Where(t => only.Contains(t.Id)).ToList();
        }
        else if (command != "revert")   // revert откатывает всё, что есть в каталоге (в т. ч. карточки, ставшие proposed/rejected)
        {
            // По умолчанию применяются только принятые карточки; proposed/rejected — только явно через --only.
            var excluded = tweaks.Where(t => !IsActive(t)).Select(t => $"{t.Id} ({t.Status})").ToList();
            if (excluded.Count > 0) Console.Error.WriteLine($"Пропущены карточки со статусом proposed/rejected: {string.Join(", ", excluded)}");
            tweaks = tweaks.Where(IsActive).ToList();
        }
        if (allowUcpdDisable && command is "apply" or "revert")
        {
            // apply: отключение UCPD должно выполниться первым, чтобы прямая запись защищённых ключей могла пройти
            // (после перезагрузки). revert: Revert() сам переворачивает порядок действий, поэтому "первым" здесь
            // означает "последним при откате" — значения защищённых ключей восстанавливаются, пока защита ещё снята.
            tweaks = new[] { UcpdGuard.DisableProtectionTweak() }.Concat(tweaks).ToList();
            Console.Error.WriteLine("--allow-ucpd-disable: защита UCPD будет отключена целиком (D-032), не только обойдена (D-031). Требуется перезагрузка, чтобы это вступило в силу.");
        }

        if (tweaks.Count == 0) return Fail("Нет твиков для выполнения.");

        if (command is "apply" or "revert" && !IsAdministrator())
            return Fail("Команда требует прав администратора (запустите от имени администратора).");

        if (command is "apply" or "revert")
        {
            new JsonRunStore(dataDir).SecureRoot();
            if (!RunLock.TryAcquire(dataDir, out var lk, out var holder))
                return Fail($"Другой прогон уже выполняется (замок {Path.Combine(dataDir, RunLock.FileName)}: {holder}).");
            using (lk) return Execute(command, tweaks, dataDir, json, ResolveResources(resourcesDir, tweaksDir));
        }
        return Execute(command, tweaks, dataDir, json, ResolveResources(resourcesDir, tweaksDir));
    }

    private static string ResolveResources(string given, string catalogDir)
        => given != "" ? given : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalogDir)) ?? ".", "resources");

    private static int Execute(string command, IReadOnlyList<Tweak> tweaks, string dataDir, bool json, string resourcesDir)
    {
        using var registry = new RegistryAccess();
        // Обход блокировки UCPD через локальную групповую политику (D-031): пишем через gpsvc, когда прямая запись
        // запрещена ОС. Защиту не отключает, поэтому включён всегда, в отличие от отключения самой службы UCPD.
        var env = new EngineEnv(registry, new ServiceAccess(), () => DateTimeOffset.Now, new TaskAccess(), new PolicyRegistryWriter(registry), new AppxAccess(),
            new BareOS.Core.Windows.FileAccess(resourcesDir, Path.Combine(dataDir, "backups")));
        // миллисекунды + суффикс: два прогона в одну секунду не должны делить каталог (baseline не перезаписывается → preflight падал бы)
        var runId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..4];
        var jsonStore = new JsonRunStore(dataDir);
        // Не-админ не пишет в корень прогонов: каталог, созданный обычным пользователем, подменил бы доверенное хранилище baseline.
        IRunStore store = command is "apply" or "revert" || IsAdministrator() ? jsonStore : new NullRunStore(jsonStore);
        var engine = new TweakEngine(env, store, runId);

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

    private static string ProofDir() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BareOS", "proof");

    private static bool IsActive(Tweak t) => t.Status is null || t.Status is "accepted" or "verified";

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
        if (r.Entries.Any(e => e.Status == ActionStatus.BlockedByOS))
            Console.WriteLine($"  Примечание: BlockedByOS — {UcpdGuard.Reason}. Это не ошибка Bare OS и не влияет на код выхода;"
                + " Bare OS пробует обход через групповую политику (D-031), полностью отключить защиту — флагом --allow-ucpd-disable (D-032).");
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
