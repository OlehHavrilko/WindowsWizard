using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BareOS.Core.Engine;

public sealed class TweakValidationException : Exception
{
    public TweakValidationException(string message) : base(message) { }
}

/// <summary>Загрузка и валидация карточек твиков из YAML (каталог catalog/, ключи camelCase, enum строками).</summary>
public static class TweakCatalog
{
    private static readonly string[] ForbiddenPathParts = { "Windows Defender", "Windows Advanced Threat Protection", "Hyper-V", "\\Microsoft\\Virtualization", "\\Lxss", "\\WSL" };
    private static readonly HashSet<string> ForbiddenServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "WinDefend", "Sense", "WdNisSvc", "WdNisDrv", "WdFilter", "WdBoot",
        "vmcompute", "vmms", "HvHost", "vmickvpexchange", "vmicheartbeat", "vmicshutdown", "vmicvss", "vmicrdv", "vmictimesync", "vmicguestinterface",
        "WSLService", "LxssManager", "lxss",
    };

    private static readonly System.Text.RegularExpressions.Regex AppxNameRx = new(@"^[A-Za-z0-9._*-]{3,128}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>Приложения, которые каталог удалять не вправе (D-033): Store, Xbox-стек, Defender/Security, WSL, winget, среды выполнения, оболочка.</summary>
    private static readonly System.Text.RegularExpressions.Regex FeatureNameRx = new(@"^[A-Za-z0-9._-]{3,128}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    /// <summary>Компоненты Windows, которые каталог отключать не вправе (D-035): Hyper-V и виртуализация, WSL, контейнеры, Defender. Сравнение по вхождению, без учёта регистра.</summary>
    private static readonly string[] ForbiddenFeatures =
    {
        "Hyper-V", "Microsoft-Hyper-V", "VirtualMachinePlatform", "Microsoft-Windows-Subsystem-Linux", "HypervisorPlatform",
        "Containers", "Windows-Defender",
    };
    private static readonly string[] FileDestPrefixes = { "%WINDIR%\\Web\\", "%ProgramData%\\BareOS\\" };

    private static readonly string[] ProtectedAppx =
    {
        "WindowsStore", "StorePurchaseApp", "Xbox", "GamingApp", "SecHealthUI", "SecurityHealth", "WindowsDefender",
        "WindowsSubsystemForLinux", "DesktopAppInstaller", "VCLibs", "NET.Native", "UI.Xaml", "WindowsAppRuntime",
        "WindowsTerminal", "ShellExperienceHost", "StartMenuExperienceHost", "Windows.Search", "CBS", "ImmersiveControlPanel",
        "AccountsControl", "LockApp", "WebView2", "Edge",
    };

    public static IReadOnlyList<Tweak> LoadDirectory(string dir)
    {
        if (!Directory.Exists(dir)) throw new TweakValidationException($"Каталог не найден: {dir}");
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var all = new List<Tweak>();
        foreach (var f in files) all.AddRange(Parse(f));
        CheckUnique(all);
        return all;
    }

    public static IReadOnlyList<Tweak> LoadFile(string path)
    {
        var list = Parse(path);
        CheckUnique(list);
        return list;
    }

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    // DTO: YamlDotNet не умеет позиционные record, поэтому читаем в изменяемые классы и маппим в контракт.
    private sealed class TweakDto
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Verdict { get; set; }
        public string? Stability { get; set; }
        public string? Intent { get; set; }
        public string? Evidence { get; set; }
        public List<ActionDto>? Actions { get; set; }
        public string? Category { get; set; }
        public string? Scope { get; set; }
        public string? Risk { get; set; }
        public string? Reversibility { get; set; }
        public string? Gate { get; set; }
        public string? Rationale { get; set; }
        public string? Breaks { get; set; }
        public string? Verification { get; set; }
        public string? Delay { get; set; }
        public string? Source { get; set; }
        public string? Status { get; set; }
    }
    private sealed class ActionDto
    {
        public string? Id { get; set; }
        public RegistryDto? Registry { get; set; }
        public ServiceDto? Service { get; set; }
        public TaskDto? Task { get; set; }
        public AppxDto? Appx { get; set; }
        public FileDto? File { get; set; }
        public FeatureDto? Feature { get; set; }
        public CleanupDto? Cleanup { get; set; }
    }
    private sealed class FeatureDto
    {
        public string? Name { get; set; }
        public string? State { get; set; }   // Enabled | Disabled
    }
    private sealed class CleanupDto
    {
        public string? Kind { get; set; }    // ComponentStore | Temp
    }
    private sealed class FileDto
    {
        public string? Source { get; set; }
        public string? Dest { get; set; }
    }
    private sealed class AppxDto
    {
        public string? Name { get; set; }
    }
    private sealed class TaskDto
    {
        public string? Path { get; set; }
        public string? State { get; set; }   // enabled | disabled
    }
    private sealed class RegistryDto
    {
        public string? Path { get; set; }
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? Data { get; set; }
    }
    private sealed class ServiceDto
    {
        public string? Name { get; set; }
        public string? Start { get; set; }
    }

    private static List<Tweak> Parse(string path)
    {
        if (!File.Exists(path)) throw new TweakValidationException($"Файл не найден: {path}");
        var result = new List<Tweak>();
        try
        {
            var text = File.ReadAllText(path);
            var root = Yaml.Deserialize<object?>(text);
            if (root is System.Collections.IList)
            {
                var dtos = Yaml.Deserialize<List<TweakDto>>(text);
                foreach (var d in dtos) result.Add(Map(d, path));
            }
            else if (root is System.Collections.IDictionary)
                result.Add(Map(Yaml.Deserialize<TweakDto>(text), path));
            else throw new TweakValidationException($"{path}: ожидается карточка (mapping) или список карточек");
        }
        catch (YamlException ex)
        {
            throw new TweakValidationException($"{path}: некорректный YAML: {ex.Message}");
        }
        foreach (var t in result) Validate(t, path);
        return result;
    }

    private static T ParseEnum<T>(string? v, string field, string path, T? defaultValue = null) where T : struct, Enum
    {
        if (v == null)
        {
            if (defaultValue is { } d) return d;
            throw new TweakValidationException($"{path}: не задано поле '{field}'");
        }
        if (Enum.TryParse<T>(v.Replace("-", ""), true, out var r) && Enum.IsDefined(r)) return r;
        throw new TweakValidationException($"{path}: недопустимое значение '{v}' поля '{field}'");
    }

    private static Tweak Map(TweakDto d, string path)
    {
        if (d == null) throw new TweakValidationException($"{path}: пустая карточка");
        var actions = (d.Actions ?? new()).Select(a =>
        {
            RegistryTarget? reg = a.Registry is { } r
                ? new RegistryTarget(r.Path ?? "", r.Name!, ParseEnum<RegKind>(r.Kind, "registry.kind", path), r.Data!)
                : null;
            ServiceTarget? svc = a.Service is { } s
                ? new ServiceTarget(s.Name ?? "", ParseEnum<StartType>(s.Start, "service.start", path))
                : null;
            TaskTarget? task = null;
            if (a.Task is { } tk)
            {
                var st = (tk.State ?? "").Trim().ToLowerInvariant();
                if (st != "enabled" && st != "disabled")
                    throw new TweakValidationException($"{path}: task.state должен быть enabled или disabled, получено '{tk.State}'");
                task = new TaskTarget(tk.Path ?? "", st == "enabled");
            }
            AppxTarget? appx = a.Appx is { } ax ? new AppxTarget(ax.Name ?? "") : null;
            FileTarget? file = a.File is { } fl ? new FileTarget(fl.Source ?? "", fl.Dest ?? "") : null;
            FeatureTarget? feature = null;
            if (a.Feature is { } fe)
            {
                var st = (fe.State ?? "").Trim().ToLowerInvariant();
                if (st != "enabled" && st != "disabled")
                    throw new TweakValidationException($"{path}: feature.state должен быть Enabled или Disabled, получено '{fe.State}'");
                feature = new FeatureTarget(fe.Name ?? "", st == "enabled");
            }
            CleanupTarget? cleanup = a.Cleanup is { } cl ? new CleanupTarget(ParseEnum<CleanupKind>(cl.Kind, "cleanup.kind", path)) : null;
            return new TweakAction(a.Id ?? "", reg, svc, task, appx, file, feature, cleanup);
        }).ToList();
        return new Tweak(d.Id ?? "", d.Title ?? "",
            ParseEnum<Verdict>(d.Verdict, "verdict", path), ParseEnum<Stability>(d.Stability, "stability", path),
            d.Intent ?? "", d.Evidence ?? "", actions,
            d.Category, d.Scope, d.Risk, d.Reversibility, d.Gate, d.Rationale, d.Breaks, d.Verification, d.Delay, d.Source, d.Status);
    }

    private static void CheckUnique(List<Tweak> all)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in all)
            if (!seen.Add(t.Id)) throw new TweakValidationException($"Повторяющийся Id твика: '{t.Id}'");
    }

    /// <summary>Данные должны совпадать с тем, как их прочитает RegistryAccess, иначе verify никогда не сойдётся,
    /// а ошибка парсинга вылезет посреди apply.</summary>
    private static bool IsCanonicalData(RegKind kind, string data) => kind switch
    {
        RegKind.DWord => uint.TryParse(data, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var u)
                         && u.ToString(System.Globalization.CultureInfo.InvariantCulture) == data,
        RegKind.QWord => ulong.TryParse(data, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var q)
                         && q.ToString(System.Globalization.CultureInfo.InvariantCulture) == data,
        RegKind.Binary => data.Length % 2 == 0 && data.All(Uri.IsHexDigit),
        _ => true
    };

    public static void Validate(Tweak t, string source = "")
    {
        string P(string m) => $"{(source == "" ? "" : source + ": ")}твик '{t.Id}': {m}";
        if (string.IsNullOrWhiteSpace(t.Id)) throw new TweakValidationException(P("пустой Id"));
        if (string.IsNullOrWhiteSpace(t.Title)) throw new TweakValidationException(P("пустой Title"));
        if (string.IsNullOrWhiteSpace(t.Evidence))
            throw new TweakValidationException(P("пустой Evidence (без измеримого эффекта твик не входит)"));
        if (t.Actions == null || t.Actions.Count == 0) throw new TweakValidationException(P("нет действий"));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in t.Actions)
        {
            if (a == null || string.IsNullOrWhiteSpace(a.Id)) throw new TweakValidationException(P("действие без Id"));
            if (!ids.Add(a.Id)) throw new TweakValidationException(P($"повторяющийся Id действия '{a.Id}'"));
            if ((a.Registry != null ? 1 : 0) + (a.Service != null ? 1 : 0) + (a.Task != null ? 1 : 0) + (a.Appx != null ? 1 : 0) + (a.File != null ? 1 : 0)
                + (a.Feature != null ? 1 : 0) + (a.Cleanup != null ? 1 : 0) != 1)
                throw new TweakValidationException(P($"действие '{a.Id}': должно быть ровно одно из Registry/Service/Task/Appx/File/Feature/Cleanup"));
            if (a.Feature is { } ft)
            {
                if (!FeatureNameRx.IsMatch(ft.Name ?? ""))
                    throw new TweakValidationException(P($"действие '{a.Id}': имя компонента '{ft.Name}' должно состоять из 3–128 символов [A-Za-z0-9._-]"));
                foreach (var bad in ForbiddenFeatures)
                    if ((ft.Name ?? "").Contains(bad, StringComparison.OrdinalIgnoreCase))
                        throw new TweakValidationException(P($"действие '{a.Id}': запрещённый компонент '{ft.Name}' (Defender/Hyper-V/WSL/виртуализация/контейнеры, D-035)"));
            }
            if (a.Cleanup is { } cu && !Enum.IsDefined(cu.Kind))
                throw new TweakValidationException(P($"действие '{a.Id}': недопустимый вид очистки '{cu.Kind}'"));
            if (a.Appx is { } ap)
            {
                if (!AppxNameRx.IsMatch(ap.Name))
                    throw new TweakValidationException(P($"действие '{a.Id}': имя Appx '{ap.Name}' должно состоять из 3–128 символов [A-Za-z0-9._*-]"));
                if (ap.Name.Replace("*", "").Length < 3)
                    throw new TweakValidationException(P($"действие '{a.Id}': шаблон Appx '{ap.Name}' слишком широкий"));
                foreach (var prot in ProtectedAppx)
                    if (ap.Name.Contains(prot, StringComparison.OrdinalIgnoreCase) || prot.Contains(ap.Name.Replace("*", ""), StringComparison.OrdinalIgnoreCase))
                        throw new TweakValidationException(P($"действие '{a.Id}': защищённое приложение '{ap.Name}' (Store/Xbox/Defender/WSL/инфраструктура, D-033)"));
            }
            if (a.File is { } fl)
            {
                if (string.IsNullOrWhiteSpace(fl.Source) || fl.Source.Contains(':') || fl.Source.StartsWith('\\') || fl.Source.StartsWith('/') || fl.Source.Contains("..", StringComparison.Ordinal))
                    throw new TweakValidationException(P($"действие '{a.Id}': file.source должен быть относительным путём без '..': '{fl.Source}'"));
                var dst = fl.Dest ?? "";
                if (dst.Contains("..", StringComparison.Ordinal) || !FileDestPrefixes.Any(x => dst.StartsWith(x, StringComparison.OrdinalIgnoreCase)) || dst.EndsWith("\\", StringComparison.Ordinal))
                    throw new TweakValidationException(P($"действие '{a.Id}': file.dest должен начинаться с одного из {string.Join(", ", FileDestPrefixes)} и указывать на файл: '{fl.Dest}'"));
            }
            if (a.Task is { } tt)
            {
                if (!tt.Path.StartsWith("\\", StringComparison.Ordinal) || tt.Path.Length < 3 || tt.Path.EndsWith("\\", StringComparison.Ordinal))
                    throw new TweakValidationException(P($"действие '{a.Id}': путь задачи должен начинаться с \\ и указывать на задачу: '{tt.Path}'"));
                foreach (var part in ForbiddenPathParts.Concat(new[] { "\\Microsoft\\Windows\\Windows Defender\\" }))
                    if (tt.Path.Contains(part, StringComparison.OrdinalIgnoreCase))
                        throw new TweakValidationException(P($"действие '{a.Id}': запрещённая цель (Defender/Hyper-V/WSL): '{tt.Path}'"));
            }
            if (a.Registry is { } r)
            {
                if (string.IsNullOrWhiteSpace(r.Path)) throw new TweakValidationException(P($"действие '{a.Id}': пустой путь"));
                if (!(r.Path.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) || r.Path.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) || r.Path.StartsWith("HKDU\\", StringComparison.OrdinalIgnoreCase)))
                    throw new TweakValidationException(P($"действие '{a.Id}': путь должен начинаться с HKLM\\, HKCU\\ или HKDU\\ (профиль по умолчанию): '{r.Path}'"));
                if (r.Name == null || r.Data == null) throw new TweakValidationException(P($"действие '{a.Id}': Name/Data обязательны"));
                if (!IsCanonicalData(r.Kind, r.Data))
                    throw new TweakValidationException(P($"действие '{a.Id}': значение '{r.Data}' не в каноническом виде для {r.Kind} (DWord/QWord — десятичное число без знака и ведущих нулей, Binary — hex)"));
                var svcIdx = r.Path.IndexOf("\\Services\\", StringComparison.OrdinalIgnoreCase);
                if (svcIdx >= 0)
                {
                    var rest = r.Path[(svcIdx + "\\Services\\".Length)..];
                    var svcName = rest.Split('\\')[0];
                    if (ForbiddenServices.Contains(svcName))
                        throw new TweakValidationException(P($"действие '{a.Id}': запрещённая служба в реестре '{svcName}' (Defender/Hyper-V/WSL): '{r.Path}'"));
                }
                foreach (var part in ForbiddenPathParts)
                    if (r.Path.Contains(part, StringComparison.OrdinalIgnoreCase))
                        throw new TweakValidationException(P($"действие '{a.Id}': запрещённая цель (Defender/Hyper-V/WSL): '{r.Path}'"));
            }
            if (a.Service is { } s)
            {
                if (string.IsNullOrWhiteSpace(s.Name)) throw new TweakValidationException(P($"действие '{a.Id}': пустое имя службы"));
                if (ForbiddenServices.Contains(s.Name))
                    throw new TweakValidationException(P($"действие '{a.Id}': запрещённая служба '{s.Name}' (Defender/Hyper-V/WSL)"));
                if (s.Start == StartType.Unknown)
                    throw new TweakValidationException(P($"действие '{a.Id}': тип запуска Unknown недопустим"));
            }
        }
    }
}
