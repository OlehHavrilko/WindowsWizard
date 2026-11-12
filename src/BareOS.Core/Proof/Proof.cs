using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BareOS.Core.Proof;

/// <summary>Снимок измеримых параметров системы (Proof Report, F-01). Все метрики — «меньше лучше».</summary>
public sealed record Snapshot(string Label, DateTimeOffset TakenAt, Dictionary<string, double> Metrics);

/// <summary>Описание метрики для отчёта.</summary>
public sealed record MetricInfo(string Key, string Title, string Unit);

public sealed record ProofRow(MetricInfo Info, double Before, double After)
{
    public double Delta => After - Before;
    public double Percent => Before == 0 ? 0 : Delta / Before * 100;
    /// <summary>Улучшение — значение уменьшилось.</summary>
    public bool Improved => After < Before;
    public bool Worse => After > Before;
}

public static class Proof
{
    public static readonly IReadOnlyList<MetricInfo> Known = new[]
    {
        new MetricInfo("appx.installed", "Установленных Appx-пакетов (все пользователи)", "шт."),
        new MetricInfo("appx.provisioned", "Предустановленных Appx-пакетов (для новых пользователей)", "шт."),
        new MetricInfo("proc.count", "Процессов в системе", "шт."),
        new MetricInfo("mem.usedMB", "Занято оперативной памяти", "МБ"),
        new MetricInfo("proc.workingSetMB", "Рабочий набор всех процессов", "МБ"),
        new MetricInfo("svc.autostart", "Служб с автозапуском", "шт."),
        new MetricInfo("startup.entries", "Записей автозапуска (Run)", "шт."),
        new MetricInfo("disk.windowsAppsMB", "Размер Program Files\\WindowsApps", "МБ"),
    };

    public static IReadOnlyList<ProofRow> Compare(Snapshot before, Snapshot after)
    {
        var rows = new List<ProofRow>();
        foreach (var m in Known)
            if (before.Metrics.TryGetValue(m.Key, out var b) && after.Metrics.TryGetValue(m.Key, out var a))
                rows.Add(new ProofRow(m, b, a));
        return rows;
    }

    private static string I(FormattableString f) => f.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string N(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    public static string ToText(Snapshot before, Snapshot after)
    {
        var rows = Compare(before, after);
        var sb = new StringBuilder();
        sb.AppendLine(I($"Proof Report: {before.Label} -> {after.Label}"));
        foreach (var r in rows)
        {
            var mark = r.Improved ? "лучше" : r.Worse ? "хуже" : "без изменений";
            sb.AppendLine(I($"  {r.Info.Title}: {N(r.Before)} -> {N(r.After)} {r.Info.Unit} ({(r.Delta >= 0 ? "+" : "")}{N(r.Delta)}, {(r.Percent >= 0 ? "+" : "")}{N(r.Percent)}%) — {mark}"));
        }
        sb.AppendLine(I($"Улучшено: {rows.Count(r => r.Improved)}, хуже: {rows.Count(r => r.Worse)}, без изменений: {rows.Count(r => !r.Improved && !r.Worse)}."));
        return sb.ToString();
    }

    private static string H(string s) => System.Net.WebUtility.HtmlEncode(s);

    public static string ToHtml(Snapshot before, Snapshot after, Report? verify = null)
    {
        var rows = Compare(before, after);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>Bare OS: Proof Report</title><style>")
          .Append(":root{--bg:#0b0d10;--fg:#e8eaed;--mut:#8a919c;--ok:#3ddc97;--bad:#ff5a6e;--line:#232830}")
          .Append("body{background:var(--bg);color:var(--fg);font:15px/1.5 system-ui,sans-serif;max-width:860px;margin:40px auto;padding:0 16px}")
          .Append("h1{font-size:22px}h1 span{color:var(--ok)}table{border-collapse:collapse;width:100%}td,th{padding:8px 10px;border-bottom:1px solid var(--line);text-align:right}")
          .Append("td:first-child,th:first-child{text-align:left}th{color:var(--mut);font-weight:500}.ok{color:var(--ok)}.bad{color:var(--bad)}.mut{color:var(--mut)}")
          .Append("</style></head><body><h1>b_ <span>Proof Report</span></h1>")
          .Append(I($"<p class=\"mut\">{H(before.Label)} ({before.TakenAt:yyyy-MM-dd HH:mm}) → {H(after.Label)} ({after.TakenAt:yyyy-MM-dd HH:mm})</p>"))
          .Append("<table><tr><th>Метрика</th><th>До</th><th>После</th><th>Δ</th></tr>");
        foreach (var r in rows)
        {
            var cls = r.Improved ? "ok" : r.Worse ? "bad" : "mut";
            sb.Append(I($"<tr><td>{H(r.Info.Title)}</td><td>{N(r.Before)} {H(r.Info.Unit)}</td><td>{N(r.After)} {H(r.Info.Unit)}</td>"))
              .Append(I($"<td class=\"{cls}\">{(r.Delta >= 0 ? "+" : "")}{N(r.Delta)} ({(r.Percent >= 0 ? "+" : "")}{N(r.Percent)}%)</td></tr>"));
        }
        sb.Append("</table>");
        if (verify is not null)
        {
            var ok = verify.Entries.Count(e => e.Status == ActionStatus.Verified || e.Status == ActionStatus.AlreadyApplied);
            sb.Append(I($"<h2>Проверка параметров</h2><p>Подтверждено <b class=\"ok\">{ok}</b> из {verify.Entries.Count} действий.</p>"));
            var bad = verify.Entries.Where(e => e.Status is ActionStatus.Mismatch or ActionStatus.Failed).ToList();
            if (bad.Count > 0)
            {
                sb.Append("<ul>");
                foreach (var e in bad) sb.Append(I($"<li class=\"bad\">{H(e.TweakId)}/{H(e.ActionId)}: {H(e.Status.ToString())}</li>"));
                sb.Append("</ul>");
            }
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string Serialize(Snapshot s) => JsonSerializer.Serialize(s, Opts);
    public static Snapshot Deserialize(string json) => JsonSerializer.Deserialize<Snapshot>(json, Opts) ?? throw new JsonException("Пустой снимок");
}
