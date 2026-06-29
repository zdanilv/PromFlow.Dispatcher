using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Avalonia;
using Avalonia.Media;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class EquipmentCardViewModel : ViewModelBase
{
    public const string EmptyRoutePointText = "—";

    private readonly IEquipmentCommandDispatcher _commandDispatcher;
    private readonly SignalBinding? _startBinding;
    private readonly SignalBinding? _stopBinding;
    private readonly RouteMapPaletteSettings _palette;
    private readonly bool _usesDefaultPalette;
    private string _statusText;
    private string _sendPointTitle = EmptyRoutePointText;
    private string _returnPointTitle = EmptyRoutePointText;
    private RouteObjectState _state;
    private bool _canStart;
    private bool _canStop;
    private bool _isStartChecked;
    private bool _isStopChecked;
    private bool _isStartPressed;
    private bool _isStopPressed;
    private bool _runtimeVisible = true;
    private bool _isApplyingRuntime;

    public EquipmentCardViewModel(
        EquipmentCommandCard card,
        IEquipmentCommandDispatcher commandDispatcher,
        RouteMapPaletteSettings? palette = null)
    {
        Id = card.Id;
        Title = card.Title;
        IsStaticallyVisible = card.IsVisible;
        Style = card.Style ?? new EquipmentCardStyle();
        StartButtonKind = card.StartButtonKind;
        StopButtonKind = card.StopButtonKind;
        StartOffFeedbackEnabled = card.StartOffFeedbackEnabled;
        StopOffFeedbackEnabled = card.StopOffFeedbackEnabled;
        _usesDefaultPalette = palette is null;
        _palette = palette ?? new RouteMapPaletteSettings();
        _statusText = card.StatusText;
        _state = card.State;
        _canStart = card.CanStart;
        _canStop = card.CanStop;
        _commandDispatcher = commandDispatcher;
        _startBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.StartCommand);
        _stopBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.StopCommand);
    }

    public string Id { get; }
    public string Title { get; }
    public bool IsStaticallyVisible { get; }
    public EquipmentCardStyle Style { get; }
    public RouteCommandButtonKind StartButtonKind { get; }
    public RouteCommandButtonKind StopButtonKind { get; }
    public bool StartOffFeedbackEnabled { get; }
    public bool StopOffFeedbackEnabled { get; }
    public bool IsStartToggle => StartButtonKind == RouteCommandButtonKind.Toggle;
    public bool IsStopToggle => StopButtonKind == RouteCommandButtonKind.Toggle;
    public bool IsVisible => IsStaticallyVisible && _runtimeVisible;
    public double CardWidth => Style.Width;
    public double MinimumCardWidth => Style.MinimumWidth;
    public double CardHeight => Style.Height;
    public Thickness CardMargin => ToThickness(Style.Margin);
    public Thickness CardPadding => ToThickness(Style.Padding);
    public Thickness CardBorderThickness => ToThickness(Style.BorderThickness);
    public CornerRadius CardCornerRadius => new(Style.CornerRadius.TopLeft, Style.CornerRadius.TopRight, Style.CornerRadius.BottomRight, Style.CornerRadius.BottomLeft);
    public IBrush BackgroundBrush => RouteMapPalette.Brush(Style.BackgroundColor);
    public IBrush BorderBrush => RouteMapPalette.Brush(Style.BorderColor);
    public IBrush TitleBrush => RouteMapPalette.Brush(Style.TitleColor);
    public IBrush TextBrush => RouteMapPalette.Brush(Style.TextColor);
    public double TitleFontSize => Style.TitleFontSize;
    public double StatusFontSize => Style.StatusFontSize;
    public double RouteTextFontSize => Style.RouteTextFontSize;
    public double ActionFontSize => Style.ActionFontSize;
    public string StartText => Style.StartText;
    public string StopText => Style.StopText;
    public IBrush StartBackground => RouteMapPalette.Brush(StartStateColor(Style.StartColor, Style.StartPressedColor, Style.StartCheckedColor));
    public IBrush StartForeground => RouteMapPalette.Brush(StartStateColor(Style.StartForegroundColor, Style.StartPressedForegroundColor, Style.StartCheckedForegroundColor));
    public IBrush StopBackground => RouteMapPalette.Brush(StopStateColor(Style.StopColor, Style.StopPressedColor, Style.StopCheckedColor));
    public IBrush StopForeground => RouteMapPalette.Brush(StopStateColor(Style.StopForegroundColor, Style.StopPressedForegroundColor, Style.StopCheckedForegroundColor));

    public string SendPointTitle
    {
        get => _sendPointTitle;
        private set
        {
            if (_sendPointTitle == value)
                return;

            this.RaiseAndSetIfChanged(ref _sendPointTitle, value);
            this.RaisePropertyChanged(nameof(SendPointText));
        }
    }

    public string ReturnPointTitle
    {
        get => _returnPointTitle;
        private set
        {
            if (_returnPointTitle == value)
                return;

            this.RaiseAndSetIfChanged(ref _returnPointTitle, value);
            this.RaisePropertyChanged(nameof(ReturnPointText));
        }
    }

    public string SendPointText => $"{Style.SendPrefix}: {SendPointTitle}";
    public string ReturnPointText => $"{Style.ReturnPrefix}: {ReturnPointTitle}";

    public string StatusText
    {
        get => _statusText;
        set
        {
            this.RaiseAndSetIfChanged(ref _statusText, value);
            this.RaisePropertyChanged(nameof(StatusBrush));
        }
    }

    public RouteObjectState State
    {
        get => _state;
        set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(StateBrush));
        }
    }

    public bool CanStart
    {
        get => _canStart;
        set => this.RaiseAndSetIfChanged(ref _canStart, value);
    }

    public bool CanStop
    {
        get => _canStop;
        set => this.RaiseAndSetIfChanged(ref _canStop, value);
    }

    public bool IsStartChecked
    {
        get => _isStartChecked;
        set
        {
            if (_isStartChecked == value)
                return;

            SetStartChecked(value);

            if (!_isApplyingRuntime)
            {
                if (value)
                {
                    SetStopChecked(false);
                    _ = DispatchStartAsync();
                }
                else
                {
                    _ = DispatchAsync(_startBinding, false);
                }
            }
        }
    }

    public bool IsStopChecked
    {
        get => _isStopChecked;
        set
        {
            if (_isStopChecked == value)
                return;

            SetStopChecked(value);

            if (!_isApplyingRuntime)
            {
                if (value)
                {
                    SetStartChecked(false);
                    _ = DispatchStopAsync();
                }
                else
                {
                    _ = DispatchAsync(_stopBinding, false);
                }
            }
        }
    }

    public bool IsStartPressed
    {
        get => _isStartPressed;
        set
        {
            if (_isStartPressed == value)
                return;

            this.RaiseAndSetIfChanged(ref _isStartPressed, value);
            this.RaisePropertyChanged(nameof(StartBackground));
            this.RaisePropertyChanged(nameof(StartForeground));
        }
    }

    public bool IsStopPressed
    {
        get => _isStopPressed;
        set
        {
            if (_isStopPressed == value)
                return;

            this.RaiseAndSetIfChanged(ref _isStopPressed, value);
            this.RaisePropertyChanged(nameof(StopBackground));
            this.RaisePropertyChanged(nameof(StopForeground));
        }
    }

    public IBrush StateBrush => State switch
    {
        RouteObjectState.Ready => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        RouteObjectState.Running => PaletteBrush(_palette.Running, RouteMapPalette.RunningBrush),
        RouteObjectState.ActiveRoute => PaletteBrush(_palette.ActiveTrack, RouteMapPalette.TrackActiveBrush),
        RouteObjectState.Warning => PaletteBrush(_palette.Warning, RouteMapPalette.WarningBrush),
        RouteObjectState.Fault => PaletteBrush(_palette.Fault, RouteMapPalette.FaultBrush),
        RouteObjectState.Offline => PaletteBrush(_palette.Offline, RouteMapPalette.OfflineBrush),
        RouteObjectState.Disabled => PaletteBrush(_palette.Disabled, RouteMapPalette.DisabledBrush),
        _ => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
    };

    public IBrush StatusBrush => StatusText switch
    {
        "Ожидание" => PaletteBrush(_palette.Warning, RouteMapPalette.WarningBrush),
        "Выключено" => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
        "Выключен" => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
        "Авария" => PaletteBrush(_palette.Fault, RouteMapPalette.FaultBrush),
        "Выполнение" => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        "Выгрузка" => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        "Загрузка" => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        _ => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
    };

    public void ApplyRuntime(RouteObjectRuntimeState? runtimeState)
    {
        if (runtimeState is null)
            return;

        _isApplyingRuntime = true;
        try
        {
            State = runtimeState.State;
            StatusText = runtimeState.Text ?? StatusText;
            CanStart = runtimeState.CanStart;
            CanStop = runtimeState.CanStop;
            var isStartChecked = runtimeState.IsStartChecked && !runtimeState.IsStopChecked;
            IsStartChecked = isStartChecked;
            IsStopChecked = runtimeState.IsStopChecked;
            _runtimeVisible = runtimeState.IsVisible;
            this.RaisePropertyChanged(nameof(IsVisible));
        }
        finally
        {
            _isApplyingRuntime = false;
        }
    }

    public void ApplyRouteSelection(
        IReadOnlyList<RouteNode> chainNodes,
        IReadOnlyDictionary<string, RouteNodeRoleState> roleStates)
    {
        SendPointTitle = SelectedPointTitle(chainNodes, roleStates, x => x.IsTarget);
        ReturnPointTitle = SelectedPointTitle(chainNodes, roleStates, x => x.IsLoader);
    }

    private Task DispatchAsync(SignalBinding? binding, bool value)
    {
        if (binding is null)
            return Task.CompletedTask;

        return _commandDispatcher.DispatchAsync(
            new SignalWriteRequest(binding.SignalId, value, binding.ValueType));
    }

    private async Task DispatchStartAsync()
    {
        await DispatchAsync(_stopBinding, false);
        await DispatchAsync(_startBinding, true);
    }

    private async Task DispatchStopAsync()
    {
        await DispatchAsync(_startBinding, false);
        await DispatchAsync(_stopBinding, true);
    }

    private void SetStartChecked(bool value)
    {
        if (_isStartChecked == value)
            return;

        this.RaiseAndSetIfChanged(ref _isStartChecked, value, nameof(IsStartChecked));
        this.RaisePropertyChanged(nameof(StartBackground));
        this.RaisePropertyChanged(nameof(StartForeground));
    }

    private void SetStopChecked(bool value)
    {
        if (_isStopChecked == value)
            return;

        this.RaiseAndSetIfChanged(ref _isStopChecked, value, nameof(IsStopChecked));
        this.RaisePropertyChanged(nameof(StopBackground));
        this.RaisePropertyChanged(nameof(StopForeground));
    }

    private static string SelectedPointTitle(
        IEnumerable<RouteNode> chainNodes,
        IReadOnlyDictionary<string, RouteNodeRoleState> roleStates,
        Func<RouteNodeRoleState, bool> predicate)
    {
        var node = chainNodes.FirstOrDefault(x =>
            roleStates.TryGetValue(x.Id, out var state) && predicate(state));

        return node is null
            ? EmptyRoutePointText
            : NormalizeTitle(node.Title);
    }

    private static string NormalizeTitle(string title)
    {
        return string.Join(" ", title.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Thickness ToThickness(RouteThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);

    private string StartStateColor(string normal, string pressed, string @checked) =>
        IsStartPressed ? pressed : IsStartChecked ? @checked : normal;

    private string StopStateColor(string normal, string pressed, string @checked) =>
        IsStopPressed ? pressed : IsStopChecked ? @checked : normal;

    private IBrush PaletteBrush(string color, IBrush defaultBrush) =>
        _usesDefaultPalette ? defaultBrush : RouteMapPalette.Brush(color);
}
