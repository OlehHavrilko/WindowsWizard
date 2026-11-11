namespace BareOS.Core;

// ===== Контракты движка. Реализации и тесты пишутся против них. =====

/// <summary>Тип значения реестра.</summary>
public enum RegKind { DWord, QWord, String, ExpandString, MultiString, Binary }

/// <summary>Значение реестра как данные: существует ли, тип, данные в строковом каноническом виде
/// (DWord/QWord — десятичное число, Binary — hex, MultiString — через '\n').</summary>
public sealed record RegValue(bool Exists, RegKind Kind = RegKind.String, string? Data = null)
{
    public static readonly RegValue Missing = new(false);
}

/// <summary>Доступ к реестру. Windows-реализация — RegistryAccess, в тестах — FakeRegistry.</summary>
public interface IRegistryAccess
{
    RegValue Read(string path, string name);                 // path: "HKLM\SOFTWARE\..."
    void Write(string path, string name, RegValue value);    // Exists=false => удалить значение
    bool KeyExists(string path);
    /// <summary>Удаляет ключ, только если в нём нет ни значений, ни подключей (откат ключей, созданных apply).</summary>
    void DeleteKeyIfEmpty(string path);
}

/// <summary>Доступ к службам Windows.</summary>
/// <summary>Тип запуска. AutomaticDelayed = Start=2 и DelayedAutostart=1 (иначе откат терял бы «отложенный запуск»).
/// Новые значения добавляются только в конец: baseline хранит enum строкой, порядок не важен, но имена — да.</summary>
public enum StartType { Boot, System, Automatic, Manual, Disabled, Unknown, AutomaticDelayed }
/// <param name="Running">Служба запущена в момент чтения; не участвует в сравнении «применено/не применено», нужна для остановки при apply и запуска при revert.</param>
public sealed record ServiceState(bool Exists, StartType Start = StartType.Unknown, bool Running = false);
public interface IServiceAccess
{
    ServiceState Read(string name);
    void SetStartType(string name, StartType start);
    void StopService(string name);
    void StartService(string name);
}

/// <summary>Состояние задачи планировщика. Enabled значим только при Exists.</summary>
public sealed record TaskState(bool Exists, bool Enabled = false);
public interface ITaskAccess
{
    TaskState Read(string path);                  // path: "\Microsoft\Windows\...\Имя"
    void SetEnabled(string path, bool enabled);
}

/// <summary>Среда выполнения: всё, что трогает систему. Tasks необязателен (null => действия task недоступны).</summary>
public sealed record EngineEnv(IRegistryAccess Registry, IServiceAccess Services, Func<DateTimeOffset> Now, ITaskAccess? Tasks = null);

// ===== Декларативное описание твика =====

public enum Stability { Stable, Moderate, Fragile }      // класс устойчивости к обновлениям (D-024)
public enum Verdict { Core, Standard, Option, Lab }

/// <summary>Одно действие твика. Ровно одно из: Registry, Service или Task.</summary>
public sealed record TweakAction(
    string Id,
    RegistryTarget? Registry = null,
    ServiceTarget? Service = null,
    TaskTarget? Task = null);

public sealed record RegistryTarget(string Path, string Name, RegKind Kind, string Data);
public sealed record ServiceTarget(string Name, StartType Start);
/// <summary>Задача планировщика: ожидаемое состояние Enabled. Для Enabled=false отсутствие задачи считается выполненным.</summary>
public sealed record TaskTarget(string Path, bool Enabled);

/// <summary>Карточка твика (читается из YAML каталога catalog/, ключи camelCase; PARAMETERS §2).
/// Поля после Actions необязательны.</summary>
public sealed record Tweak(
    string Id,                       // "privacy.telemetry"
    string Title,
    Verdict Verdict,
    Stability Stability,
    string Intent,                   // что и зачем
    string Evidence,                 // чем измеряется эффект (требование: без измеримого эффекта твик не входит)
    IReadOnlyList<TweakAction> Actions,
    string? Category = null,         // registry / service / appx / task / privacy / convenience / latency
    string? Scope = null,            // machine / user
    string? Risk = null,             // T0..T4 (SAFETY)
    string? Reversibility = null,    // что нужно для возврата; "irreversible" фиксируется явно
    string? Gate = null,             // option / FeaturePage
    string? Rationale = null,        // зачем
    string? Breaks = null,           // что ломает
    string? Verification = null,     // как верификатор подтверждает результат
    string? Delay = null,            // none / reboot / relogon
    string? Source = null,           // источник и применимость
    string? Status = null);          // proposed / accepted / verified / rejected

// ===== План, снимок, отчёт =====

public enum ActionStatus { Planned, Started, Applied, AlreadyApplied, Failed, Skipped, Unknown, Verified, Mismatch, Reverted, Conflict, NothingToRevert,
    /// <summary>Запись/удаление запретила сама ОС (драйвер UCPD защищает ключ, D-030). Не ошибка движка: apply/revert/verify не дают из-за него код 1.
    /// Добавлено в конец: имена в журналах и отчётах строковые.</summary>
    BlockedByOS }

public sealed record PlanEntry(string TweakId, string ActionId, string Target, string Before, string Expected);
public sealed record Plan(string FormatVersion, string RunId, DateTimeOffset CreatedAt, IReadOnlyList<PlanEntry> Entries);

/// <summary>Снимок исходного состояния одной цели. Одно из Registry/Service/Task заполнено.</summary>
/// <param name="KeyExisted">Для реестра: существовал ли ключ до apply (если нет — revert удаляет его, когда он пуст).</param>
public sealed record BaselineEntry(string TweakId, string ActionId, RegValue? Registry, ServiceState? Service, TaskState? Task = null, bool? KeyExisted = null);
/// <param name="IsRevertMarker">Снимок, записанный после успешного отката: состояние целей после revert. Граница истории —
/// PickBaseline не заглядывает за него, поэтому уже откатанный apply не «воскресает» при следующем откате.</param>
public sealed record Baseline(string FormatVersion, string BaselineId, DateTimeOffset CreatedAt, IReadOnlyList<BaselineEntry> Entries, bool IsRevertMarker = false);

/// <summary>Запись журнала/отчёта: статус + доказательство (значение до/после/ожидаемое).</summary>
public sealed record ReportEntry(string TweakId, string ActionId, ActionStatus Status, string Before, string After, string Expected, string? Error = null);
public sealed record Report(string FormatVersion, string RunId, string Command, DateTimeOffset At, IReadOnlyList<ReportEntry> Entries)
{
    public bool Ok => Entries.All(e => e.Status is ActionStatus.Applied or ActionStatus.AlreadyApplied or ActionStatus.Verified
                                            or ActionStatus.Reverted or ActionStatus.NothingToRevert or ActionStatus.Skipped
                                            or ActionStatus.BlockedByOS);
}

/// <summary>Хранилище прогонов: %ProgramData%\BareOS\runs\&lt;runId&gt;\ (путь передаётся снаружи).
/// Baseline пишется атомарно (temp + replace) и НЕ перезаписывается.</summary>
public interface IRunStore
{
    void SaveBaseline(Baseline baseline);            // бросает, если такой baseline уже есть
    Baseline? LoadLatestBaseline();
    /// <summary>Все baseline, от старых к новым. Revert ищет по каждой цели снимок, сделанный перед реальным изменением,
    /// поэтому повторный apply (когда всё уже применено) не «затирает» исходное состояние.</summary>
    IReadOnlyList<Baseline> LoadBaselines();   // по возрастанию CreatedAt
    void SaveReport(Report report);
    void SaveJournalLine(string runId, ReportEntry entry);   // append перед/после каждого действия
}

// ===== Движок =====

public interface ITweakEngine
{
    Plan Plan(IReadOnlyList<Tweak> tweaks);                          // только чтение
    Report Apply(IReadOnlyList<Tweak> tweaks);                       // baseline -> preflight -> применение -> verify каждого шага
    Report Verify(IReadOnlyList<Tweak> tweaks);                      // текущее == ожидаемое
    Report Revert(IReadOnlyList<Tweak> tweaks);                      // по baseline (последний снимок цели до её изменения), в обратном порядке; 3-way: текущее/baseline/ожидаемое, иначе Conflict
}
