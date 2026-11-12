using System.ComponentModel;
using System.Runtime.CompilerServices;
using BareOS.Core;

namespace BareOS.Toolbox;

public abstract class Notify : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name!);
    }
}

/// <summary>Карточка каталога в списке.</summary>
public sealed class TweakItem : Notify
{
    private static readonly Dictionary<string, string> Groups = new()
    {
        ["appx"] = "Приложения", ["privacy"] = "Приватность", ["consumer"] = "Реклама и рекомендации",
        ["browser"] = "Браузер", ["network"] = "Сеть", ["perf"] = "Производительность", ["onedrive"] = "OneDrive",
        ["shell"] = "Оболочка", ["services"] = "Службы", ["update"] = "Обновления", ["brand"] = "Оформление",
    };

    public TweakItem(Tweak t)
    {
        Tweak = t;
        var prefix = t.Id.Split('.')[0];
        Group = Groups.TryGetValue(prefix, out var g) ? g : char.ToUpperInvariant(prefix[0]) + prefix[1..];
        Irreversible = EngineHost.IsIrreversible(t);
        Meta = $"{t.Risk ?? "—"} · {t.Stability}" + (Irreversible ? " · не восстанавливается" : "");
    }

    public Tweak Tweak { get; }
    public string Id => Tweak.Id;
    public string Title => Tweak.Title;
    public string Intent => Tweak.Intent;
    public string Breaks => string.IsNullOrWhiteSpace(Tweak.Breaks) ? "" : "Что ломает: " + Tweak.Breaks;
    public string Evidence => Tweak.Evidence;
    public string Group { get; }
    public string Meta { get; }
    public bool Irreversible { get; }

    private TweakState _state = TweakState.Unknown;
    public TweakState State { get => _state; set { Set(ref _state, value); Raise(nameof(IsOn)); Raise(nameof(StateText)); Raise(nameof(CanToggle)); Raise(nameof(StateBrush)); } }

    private bool _busy;
    public bool Busy { get => _busy; set { Set(ref _busy, value); Raise(nameof(CanToggle)); Raise(nameof(StateText)); } }

    public bool IsOn => State == TweakState.On;
    /// <summary>Нельзя выключить уже применённое необратимое (Appx); включить частично применённое — можно.</summary>
    public bool CanToggle => !Busy && State != TweakState.Unknown && !(Irreversible && State == TweakState.On);
    public string StateText => Busy ? "выполняется…" : State switch
    {
        TweakState.On => Irreversible ? "применено" : "включено",
        TweakState.Off => "выключено",
        TweakState.Partial => "частично",
        _ => "н/д",
    };
    public System.Windows.Media.Brush StateBrush => (System.Windows.Media.Brush)System.Windows.Application.Current.FindResource(
        State switch { TweakState.On => "Signal", TweakState.Partial => "Ember", _ => "Stone" });

}

public sealed record ProofLine(string Title, string Before, string After, string Delta, bool Improved, bool Worse);
