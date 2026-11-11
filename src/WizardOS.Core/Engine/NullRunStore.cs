namespace WizardOS.Core.Engine;

/// <summary>Хранилище только для чтения: читает baseline из настоящего, ничего не пишет. Для plan/verify без прав администратора.</summary>
public sealed class NullRunStore : IRunStore
{
    private readonly IRunStore _inner;
    public NullRunStore(IRunStore inner) { _inner = inner; }
    public void SaveBaseline(Baseline baseline) => throw new InvalidOperationException("Запись baseline без прав администратора запрещена");
    public Baseline? LoadLatestBaseline() => _inner.LoadLatestBaseline();
    public IReadOnlyList<Baseline> LoadBaselines() => _inner.LoadBaselines();
    public void SaveReport(Report report) { }
    public void SaveJournalLine(string runId, ReportEntry entry) { }
}
