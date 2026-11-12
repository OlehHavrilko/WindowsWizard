using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using BareOS.Core;
using BareOS.Core.Proof;

namespace BareOS.Toolbox;

public partial class MainWindow : Window
{
    private EngineHost? _host;
    private readonly ObservableCollection<TweakItem> _items = new();
    private ICollectionView? _view;
    private readonly StringBuilder _log = new();
    private bool _busy;

    public MainWindow() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { _host = EngineHost.Create(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Bare OS Toolbox", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }
        foreach (var t in _host.Tweaks.Where(EngineHost.IsActive)) _items.Add(new TweakItem(t));
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TweakItem.Group)));
        _view.SortDescriptions.Add(new SortDescription(nameof(TweakItem.Group), ListSortDirection.Ascending));
        List.ItemsSource = _view;
        Log($"Каталог: {_host.CatalogDir}, карточек: {_items.Count}");
        await RefreshAsync();
        ShowProof();
        // Замер «до» — один раз, пока система ещё не изменена Toolbox'ом (иначе Proof Report нечем сравнивать).
        if (_host.LoadSnapshot(_host.BeforePath) is null && _items.All(i => i.State != TweakState.On))
            await RunBusy("Замер «до»…", () => _host.Measure("before", _host.BeforePath));
    }

    // ---------- вспомогательное ----------
    private void Log(string line)
    {
        _log.AppendLine($"{DateTime.Now:HH:mm:ss}  {line}");
        LogBox.Text = _log.ToString();
        LogBox.ScrollToEnd();
    }

    private void SetStatus(string s) => StatusText.Text = s;

    private async Task RunBusy(string status, Action work)
    {
        if (_busy) return;
        _busy = true; SetBusyUi(true); SetStatus(status);
        try { await Task.Run(work); }
        catch (Exception ex) { Log("Ошибка: " + ex.Message); SetStatus("Ошибка: " + ex.Message); MessageBox.Show(ex.Message, "Bare OS Toolbox", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _busy = false; SetBusyUi(false); }
    }

    private void SetBusyUi(bool busy)
    {
        BtnApplyAll.IsEnabled = BtnRevertAll.IsEnabled = BtnRefresh.IsEnabled = !busy;
        List.IsEnabled = !busy;
    }

    private async Task RefreshAsync()
    {
        if (_host is null) return;
        Dictionary<string, TweakState>? states = null;
        await RunBusy("Проверка состояния…", () => states = _host.ReadStates());
        if (states is null) return;
        foreach (var i in _items) i.State = states.TryGetValue(i.Id, out var s) ? s : TweakState.Unknown;
        int on = _items.Count(i => i.State == TweakState.On);
        SetStatus($"Включено {on} из {_items.Count}");
    }

    private async Task ExecuteAsync(string verb, IReadOnlyList<Tweak> tweaks, Func<IReadOnlyList<Tweak>, Report> op)
    {
        if (_host is null || tweaks.Count == 0) return;
        foreach (var i in _items.Where(i => tweaks.Contains(i.Tweak))) i.Busy = true;
        Report? report = null;
        await RunBusy($"{verb}: {tweaks.Count} параметр(ов)…", () => report = op(tweaks));
        foreach (var i in _items) i.Busy = false;
        if (report is not null)
        {
            var bad = report.Entries.Where(e => !(e.Status is ActionStatus.Applied or ActionStatus.AlreadyApplied or ActionStatus.Verified
                or ActionStatus.Reverted or ActionStatus.NothingToRevert or ActionStatus.Skipped or ActionStatus.BlockedByOS)).ToList();
            Log($"{verb}: {report.Entries.Count} действий, проблем: {bad.Count} (прогон {report.RunId})");
            foreach (var b in bad) Log($"  ! {b.TweakId}/{b.ActionId}: {b.Status} {b.Error}");
            foreach (var s in report.Entries.Where(e => e.Status == ActionStatus.Skipped && e.Error is not null).Take(5)) Log($"  · {s.TweakId}/{s.ActionId}: {s.Error}");
            if (bad.Count > 0) MessageBox.Show($"Не удалось выполнить {bad.Count} действий. Подробности — на вкладке «Журнал».", "Bare OS Toolbox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        await RefreshAsync();
    }

    // ---------- события ----------
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PaneParams is null) return;
        PaneParams.Visibility = TabParams.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PaneProof.Visibility = TabProof.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PaneLog.Visibility = TabLog.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_view is null) return;
        var q = Search.Text.Trim();
        _view.Filter = q.Length == 0 ? null : o => o is TweakItem t &&
            (t.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) || t.Intent.Contains(q, StringComparison.CurrentCultureIgnoreCase) || t.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null || sender is not ToggleButton tb || tb.DataContext is not TweakItem item) return;
        bool wantOn = tb.IsChecked == true;
        tb.IsChecked = item.IsOn;   // состояние меняет только результат движка
        if (_busy) return;
        if (wantOn)
        {
            if (item.Irreversible && MessageBox.Show($"«{item.Title}»\n\nПриложения будут удалены без возможности отката: вернуть их можно только из Microsoft Store или winget. Продолжить?",
                    "Bare OS Toolbox", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ExecuteAsync("Применение", new[] { item.Tweak }, _host.Apply);
        }
        else
            await ExecuteAsync("Откат", new[] { item.Tweak }, _host.Revert);
    }

    private async void ApplyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var todo = _items.Where(i => i.State is TweakState.Off or TweakState.Partial).ToList();
        if (todo.Count == 0) { SetStatus("Всё рекомендованное уже включено"); return; }
        var appx = todo.Where(i => i.Irreversible).ToList();
        var msg = $"Будет применено параметров: {todo.Count}.";
        if (appx.Count > 0) msg += $"\n\nИз них {appx.Count} удаляют приложения без возможности отката (вернуть можно только из Store или winget):\n" + string.Join("\n", appx.Select(i => "• " + i.Title));
        if (MessageBox.Show(msg + "\n\nПродолжить?", "Bare OS Toolbox", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await ExecuteAsync("Применение", todo.Select(i => i.Tweak).ToList(), _host.Apply);
    }

    private async void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        var todo = _items.Where(i => i.State is TweakState.On or TweakState.Partial && !i.Irreversible).ToList();
        if (todo.Count == 0) { SetStatus("Откатывать нечего"); return; }
        if (MessageBox.Show($"Будет откатено параметров: {todo.Count} (кроме удалённых приложений). Продолжить?", "Bare OS Toolbox", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await ExecuteAsync("Откат", todo.Select(i => i.Tweak).ToList(), _host.Revert);
    }

    // ---------- Proof Report ----------
    private void ShowProof()
    {
        if (_host is null) return;
        var b = _host.LoadSnapshot(_host.BeforePath);
        var a = _host.LoadSnapshot(_host.AfterPath);
        if (b is null) { ProofSummary.Text = "Замера «до» ещё нет. Сделайте его, пока система не изменена."; ProofList.ItemsSource = null; return; }
        if (a is null) { ProofSummary.Text = $"Замер «до» от {b.TakenAt:dd.MM.yyyy HH:mm}. После изменений нажмите «Замер «после» и сравнить»."; ProofList.ItemsSource = null; return; }
        var rows = Proof.Compare(b, a);
        ProofList.ItemsSource = rows.Select(r => new ProofLine(r.Info.Title,
            Num(r.Before) + " " + r.Info.Unit, Num(r.After) + " " + r.Info.Unit,
            (r.Delta >= 0 ? "+" : "") + Num(r.Delta) + " (" + (r.Percent >= 0 ? "+" : "") + Num(r.Percent) + "%)", r.Improved, r.Worse)).ToList();
        ProofSummary.Text = $"До: {b.TakenAt:dd.MM.yyyy HH:mm} → После: {a.TakenAt:dd.MM.yyyy HH:mm}. Лучше: {rows.Count(r => r.Improved)}, хуже: {rows.Count(r => r.Worse)}, без изменений: {rows.Count(r => !r.Improved && !r.Worse)}.";
    }

    private static string Num(double v) => v.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);

    private async void MeasureBefore_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        if (_host.LoadSnapshot(_host.BeforePath) is not null &&
            MessageBox.Show("Замер «до» уже есть. Перезаписать его текущим состоянием?", "Bare OS Toolbox", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunBusy("Замер «до»…", () => _host.Measure("before", _host.BeforePath));
        Log("Замер «до» сохранён"); ShowProof(); SetStatus("Замер «до» сохранён");
    }

    private async void MeasureAfter_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        if (_host.LoadSnapshot(_host.BeforePath) is null) { MessageBox.Show("Нет замера «до». Сначала нажмите «Замер «до»».", "Bare OS Toolbox"); return; }
        await RunBusy("Замер «после»…", () =>
        {
            var a = _host.Measure("after", _host.AfterPath);
            _host.WriteHtml(_host.LoadSnapshot(_host.BeforePath)!, a);
        });
        Log("Замер «после» сохранён, Proof Report обновлён"); ShowProof(); SetStatus("Proof Report обновлён");
    }

    private void OpenHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;
        if (!File.Exists(_host.HtmlPath)) { MessageBox.Show("HTML-отчёта ещё нет: сделайте замер «после».", "Bare OS Toolbox"); return; }
        Process.Start(new ProcessStartInfo(_host.HtmlPath) { UseShellExecute = true });
    }
}
