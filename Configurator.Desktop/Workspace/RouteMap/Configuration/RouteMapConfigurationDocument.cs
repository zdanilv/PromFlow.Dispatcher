using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public abstract class RouteMapConfigurationItem : ReactiveObject
{
    private string _id = string.Empty;
    private bool _isInvalid;
    private string? _validationMessage;

    public string Id
    {
        get => _id;
        set => this.RaiseAndSetIfChanged(ref _id, value);
    }

    [JsonIgnore]
    public bool IsInvalid
    {
        get => _isInvalid;
        set => this.RaiseAndSetIfChanged(ref _isInvalid, value);
    }

    [JsonIgnore]
    public string? ValidationMessage
    {
        get => _validationMessage;
        set => this.RaiseAndSetIfChanged(ref _validationMessage, value);
    }
}

public sealed class RouteMapConfigurationDocument
{
    public const int CurrentSchemaVersion = 16;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public RouteMapSettingsConfiguration Map { get; set; } = new();
    public RouteTopBarConfiguration TopBar { get; set; } = RouteTopBarConfiguration.CreateDefault();
    public ObservableCollection<RouteChainConfiguration> Chains { get; set; } = [];
    public ObservableCollection<RouteNodeConfiguration> Nodes { get; set; } = [];
    public ObservableCollection<RouteSegmentConfiguration> Segments { get; set; } = [];
    public ObservableCollection<EquipmentCardConfiguration> Cards { get; set; } = [];
    public ObservableCollection<RoutePlaceholderRuleConfiguration> PlaceholderRules { get; set; } = [];
}

public sealed class RouteMapSettingsConfiguration
{
    public double LogicalWidth { get; set; } = 1200;
    public double LogicalHeight { get; set; } = 800;
    public double MapPadding { get; set; } = 18;
    public double CardColumnGap { get; set; } = 18;
    public double FragmentLength { get; set; } = 100;
    public double FragmentGap { get; set; } = 6;
    public RouteMapPaletteConfiguration Palette { get; set; } = new();
}

public sealed class RouteMapPaletteConfiguration
{
    public string Background { get; set; } = "#F7F8F8";
    public string Text { get; set; } = "#44505C";
    public string MutedText { get; set; } = "#77828D";
    public string Track { get; set; } = "#C9CED0";
    public string ActiveTrack { get; set; } = "#2D56B3";
    public string Ready { get; set; } = "#3A9D5D";
    public string Running { get; set; } = "#2563EB";
    public string Warning { get; set; } = "#D99B22";
    public string Fault { get; set; } = "#D95D4E";
    public string Offline { get; set; } = "#3F474D";
    public string Disabled { get; set; } = "#3F474D";
    public string NodeFill { get; set; } = "#AEB5BA";
    public string Selection { get; set; } = "#21428E";
    public string Hover { get; set; } = "#1E6BFF";
}

public sealed class RouteChainConfiguration : RouteMapConfigurationItem
{
    public double X { get; set; }
    public double Y { get; set; }
    public ObservableCollection<string> NodeIds { get; set; } = [];
    public ObservableCollection<string> SegmentIds { get; set; } = [];
}

public sealed class RouteNodeConfiguration : RouteMapConfigurationItem
{
    public string Title { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public RouteNodeKind Kind { get; set; }
    public RouteObjectState State { get; set; } = RouteObjectState.Idle;
    public double LabelOffsetX { get; set; }
    public double LabelOffsetY { get; set; }
    public RouteNodeLabelPlacement LabelPlacement { get; set; } = RouteNodeLabelPlacement.Below;
    public bool IsLoader { get; set; }
    public bool IsTarget { get; set; }
    public RouteNodeMenuKind MenuKind { get; set; }
    public bool IsVisible { get; set; } = true;
    public RouteNodeStyleConfiguration Style { get; set; } = new();
    public ObservableCollection<SignalBindingConfiguration> Bindings { get; set; } = [];
}

public sealed class RouteNodeStyleConfiguration
{
    public double Radius { get; set; } = 15;
    public double InnerRadiusRatio { get; set; } = 0.38;
    public double BorderThickness { get; set; } = 2;
    public string FillColor { get; set; } = "#AEB5BA";
    public string BorderColor { get; set; } = "#DDE1E4";
    public string InnerColor { get; set; } = "#EEF1F3";
    public string LabelColor { get; set; } = "#77828D";
    public double LabelFontSize { get; set; } = 11;
    public string ActiveOutlineColor { get; set; } = "#00A6A6";
    public double ActiveOutlineThickness { get; set; } = 3;
}

public sealed class RouteTopBarConfiguration
{
    public RouteTopBarButtonConfiguration Automatic { get; set; } = new();
    public RouteTopBarButtonConfiguration Manual { get; set; } = new();
    public RouteTopBarButtonConfiguration Reset { get; set; } = new();
    public RouteTopBarEmergencyButtonConfiguration Emergency { get; set; } = new();

    public static RouteTopBarConfiguration CreateDefault() => new()
    {
        Automatic = RouteTopBarButtonConfiguration.Create(
            "АВТОМАТ", SignalBindingRole.AutomaticModeCommand, "system.mode.automatic"),
        Manual = RouteTopBarButtonConfiguration.Create(
            "РУЧНОЙ", SignalBindingRole.ManualModeCommand, "system.mode.manual"),
        Reset = RouteTopBarButtonConfiguration.Create(
            "СБРОС", SignalBindingRole.ResetCommand, "system.reset"),
        Emergency = RouteTopBarEmergencyButtonConfiguration.Create(
            "АВАРИЯ", SignalBindingRole.EmergencyCommand, "system.emergency"),
    };
}

public class RouteTopBarButtonConfiguration : ReactiveObject
{
    public string Text { get; set; } = string.Empty;
    public ObservableCollection<SignalBindingConfiguration> Bindings { get; set; } = [];

    public static RouteTopBarButtonConfiguration Create(
        string text,
        SignalBindingRole role,
        string signalId) => new()
        {
            Text = text,
            Bindings = CreateButtonBindings(role, signalId),
        };

    protected static ObservableCollection<SignalBindingConfiguration> CreateButtonBindings(
        SignalBindingRole role,
        string signalId) =>
    [
        new()
        {
            Role = role,
            SignalId = signalId,
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Bool,
        },
    ];
}

public sealed class RouteTopBarEmergencyButtonConfiguration : RouteTopBarButtonConfiguration
{
    private RouteCommandButtonKind _buttonKind = RouteCommandButtonKind.Toggle;
    private bool _offFeedbackEnabled;

    public RouteCommandButtonKind ButtonKind
    {
        get => _buttonKind;
        set
        {
            this.RaiseAndSetIfChanged(ref _buttonKind, value);
            this.RaisePropertyChanged(nameof(IsOffFeedbackAvailable));
        }
    }

    public bool OffFeedbackEnabled
    {
        get => _offFeedbackEnabled;
        set => this.RaiseAndSetIfChanged(ref _offFeedbackEnabled, value);
    }

    [JsonIgnore]
    public bool IsOffFeedbackAvailable => false;

    public new static RouteTopBarEmergencyButtonConfiguration Create(
        string text,
        SignalBindingRole role,
        string signalId)
    {
        return new()
        {
            Text = text,
            Bindings = CreateButtonBindings(role, signalId),
            OffFeedbackEnabled = false,
        };
    }
}

public sealed class RouteSegmentConfiguration : RouteMapConfigurationItem
{
    public string FromNodeId { get; set; } = string.Empty;
    public string ToNodeId { get; set; } = string.Empty;
    public RouteObjectState State { get; set; } = RouteObjectState.Idle;
    public bool IsDirectional { get; set; }
    public RouteSegmentKind Kind { get; set; }
    public double ArcRadius { get; set; }
    public RouteElbowOrder ElbowOrder { get; set; }
    public string? Title { get; set; }
    public double LabelOffsetX { get; set; }
    public double LabelOffsetY { get; set; }
    public bool IsVisible { get; set; } = true;
    public RouteSegmentStyleConfiguration Style { get; set; } = new();
    public ObservableCollection<SignalBindingConfiguration> Bindings { get; set; } = [];
    public ObservableCollection<RouteSegmentActiveFragmentConfiguration> ActiveFragments { get; set; } = [];
}

public sealed class RouteSegmentActiveFragmentConfiguration : ReactiveObject
{
    public int Index { get; set; }
    public SignalBindingConfiguration Binding { get; set; } = new()
    {
        Role = SignalBindingRole.ActiveRouteFragment,
        Direction = SignalBindingDirection.Read,
        ValueType = SignalValueType.Bool,
    };
}

public sealed class RouteSegmentStyleConfiguration
{
    public string NormalColor { get; set; } = "#C9CED0";
    public string ActiveColor { get; set; } = "#2D56B3";
    public double Thickness { get; set; } = 4;
    public double ActiveThickness { get; set; } = 5;
    public double? FragmentLength { get; set; }
    public double? FragmentGap { get; set; }
    public double EndpointGap { get; set; } = 6;
    public RouteLineCap LineCap { get; set; } = RouteLineCap.Round;
    public string LabelColor { get; set; } = "#77828D";
    public double LabelFontSize { get; set; } = 11;
}

public sealed class EquipmentCardConfiguration : RouteMapConfigurationItem
{
    private bool _canStart = true;
    private bool _canStop = true;
    private RouteCommandButtonKind _startButtonKind = RouteCommandButtonKind.Toggle;
    private RouteCommandButtonKind _stopButtonKind = RouteCommandButtonKind.Toggle;
    private bool _startOffFeedbackEnabled;
    private bool _stopOffFeedbackEnabled;

    public string Title { get; set; } = string.Empty;
    public string StatusText { get; set; } = "Выключено";
    public RouteObjectState State { get; set; } = RouteObjectState.Idle;
    public bool CanStart
    {
        get => _canStart;
        set
        {
            this.RaiseAndSetIfChanged(ref _canStart, value);
            this.RaisePropertyChanged(nameof(IsStartOffFeedbackAvailable));
        }
    }
    public bool CanStop
    {
        get => _canStop;
        set
        {
            this.RaiseAndSetIfChanged(ref _canStop, value);
            this.RaisePropertyChanged(nameof(IsStopOffFeedbackAvailable));
        }
    }
    public RouteCommandButtonKind StartButtonKind
    {
        get => _startButtonKind;
        set
        {
            this.RaiseAndSetIfChanged(ref _startButtonKind, value);
            this.RaisePropertyChanged(nameof(IsStartOffFeedbackAvailable));
        }
    }
    public RouteCommandButtonKind StopButtonKind
    {
        get => _stopButtonKind;
        set
        {
            this.RaiseAndSetIfChanged(ref _stopButtonKind, value);
            this.RaisePropertyChanged(nameof(IsStopOffFeedbackAvailable));
        }
    }
    public bool StartOffFeedbackEnabled
    {
        get => _startOffFeedbackEnabled;
        set => this.RaiseAndSetIfChanged(ref _startOffFeedbackEnabled, value);
    }
    public bool StopOffFeedbackEnabled
    {
        get => _stopOffFeedbackEnabled;
        set => this.RaiseAndSetIfChanged(ref _stopOffFeedbackEnabled, value);
    }
    [JsonIgnore]
    public bool IsStartOffFeedbackAvailable => CanStart && StartButtonKind == RouteCommandButtonKind.Toggle;
    [JsonIgnore]
    public bool IsStopOffFeedbackAvailable => CanStop && StopButtonKind == RouteCommandButtonKind.Toggle;
    public bool IsVisible { get; set; } = true;
    public string? AttachedChainId { get; set; }
    public double AttachedCardRightOffset { get; set; }
    public RouteCardVerticalAnchorKind VerticalAnchorKind { get; set; } = RouteCardVerticalAnchorKind.ChainBoundsCenter;
    public string? VerticalAnchorNodeId { get; set; }
    public EquipmentCardStyleConfiguration Style { get; set; } = new();
    public ObservableCollection<SignalBindingConfiguration> Bindings { get; set; } = [];
    public ObservableCollection<EquipmentCardParameterConfiguration> Parameters { get; set; } = [];
}

public sealed class EquipmentCardParameterConfiguration : ReactiveObject
{
    private string _title = "Параметр";
    private SignalBindingRole _role = SignalBindingRole.EquipmentParameter;
    private string _signalId = string.Empty;
    private SignalBindingDirection _direction = SignalBindingDirection.ReadWrite;
    private SignalValueType _valueType = SignalValueType.Word;
    private string? _savedValue;
    private bool _pendingAutoDispatch;
    private string? _lastDispatchError;

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public SignalBindingRole Role
    {
        get => _role;
        set => this.RaiseAndSetIfChanged(ref _role, value);
    }

    public string SignalId
    {
        get => _signalId;
        set
        {
            if (string.Equals(_signalId, value, StringComparison.Ordinal))
                return;

            this.RaiseAndSetIfChanged(ref _signalId, value);
            ClearSavedSetpoint();
        }
    }

    public SignalBindingDirection Direction
    {
        get => _direction;
        set
        {
            if (_direction == value)
                return;

            this.RaiseAndSetIfChanged(ref _direction, value);
            ClearSavedSetpoint();
        }
    }

    public SignalValueType ValueType
    {
        get => _valueType;
        set
        {
            if (_valueType == value)
                return;

            this.RaiseAndSetIfChanged(ref _valueType, value);
            ClearSavedSetpoint();
        }
    }

    public string? SavedValue
    {
        get => _savedValue;
        set => this.RaiseAndSetIfChanged(ref _savedValue, value);
    }

    public bool PendingAutoDispatch
    {
        get => _pendingAutoDispatch;
        set => this.RaiseAndSetIfChanged(ref _pendingAutoDispatch, value);
    }

    public string? LastDispatchError
    {
        get => _lastDispatchError;
        set => this.RaiseAndSetIfChanged(ref _lastDispatchError, value);
    }

    public void ClearSavedSetpoint()
    {
        SavedValue = null;
        PendingAutoDispatch = false;
        LastDispatchError = null;
    }
}

public sealed class EquipmentCardStyleConfiguration
{
    public double Width { get; set; } = 295;
    public double MinimumWidth { get; set; } = 250;
    public double Height { get; set; } = 141;
    public RouteThicknessConfiguration Margin { get; set; } = RouteThicknessConfiguration.Uniform(5);
    public RouteThicknessConfiguration Padding { get; set; } = new() { Left = 12, Top = 8, Right = 6, Bottom = 8 };
    public string BackgroundColor { get; set; } = "#00FFFFFF";
    public string BorderColor { get; set; } = "#C8D0D7";
    public RouteThicknessConfiguration BorderThickness { get; set; } = new() { Left = 2 };
    public RouteCornerRadiusConfiguration CornerRadius { get; set; } = new();
    public string TitleColor { get; set; } = "#48525C";
    public string TextColor { get; set; } = "#48525C";
    public double TitleFontSize { get; set; } = 18;
    public double StatusFontSize { get; set; } = 16;
    public double RouteTextFontSize { get; set; } = 14;
    public double ActionFontSize { get; set; } = 18;
    public string StartText { get; set; } = "ПУСК";
    public string StopText { get; set; } = "СТОП";
    public string SendPrefix { get; set; } = "Отправить";
    public string ReturnPrefix { get; set; } = "Возврат";
}

public sealed class RoutePlaceholderRuleConfiguration : RouteMapConfigurationItem
{
    public string CardId { get; set; } = string.Empty;
    public RoutePlaceholderPlacement Placement { get; set; } = RoutePlaceholderPlacement.Both;
    public RoutePlaceholderHeightMode HeightMode { get; set; } = RoutePlaceholderHeightMode.MatchCard;
    public double FixedHeight { get; set; } = 141;
    public double Gap { get; set; } = 10;
    public int? MaximumCount { get; set; }
    public bool IsVisible { get; set; } = true;
    public RoutePlaceholderStyleConfiguration Style { get; set; } = new();
}

public sealed class RoutePlaceholderStyleConfiguration
{
    public string BackgroundColor { get; set; } = "#00FFFFFF";
    public string BorderColor { get; set; } = "#C8D0D7";
    public RouteThicknessConfiguration BorderThickness { get; set; } = new() { Left = 2 };
    public RouteCornerRadiusConfiguration CornerRadius { get; set; } = new();
    public RouteThicknessConfiguration Margin { get; set; } = RouteThicknessConfiguration.Uniform(5);
}

public sealed class RouteThicknessConfiguration
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; }
    public double Bottom { get; set; }

    public static RouteThicknessConfiguration Uniform(double value) =>
        new() { Left = value, Top = value, Right = value, Bottom = value };
}

public sealed class RouteCornerRadiusConfiguration
{
    public double TopLeft { get; set; }
    public double TopRight { get; set; }
    public double BottomRight { get; set; }
    public double BottomLeft { get; set; }
}

public sealed class SignalBindingConfiguration
{
    public SignalBindingRole Role { get; set; }
    public string SignalId { get; set; } = string.Empty;
    public SignalBindingDirection Direction { get; set; }
    public SignalValueType ValueType { get; set; }
}
