using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Settings;

public sealed partial class SignalBindingsEditor : UserControl
{
    public static readonly StyledProperty<IEnumerable?> BindingsProperty =
        AvaloniaProperty.Register<SignalBindingsEditor, IEnumerable?>(nameof(Bindings));

    public static readonly StyledProperty<ICommand?> AddCommandProperty =
        AvaloniaProperty.Register<SignalBindingsEditor, ICommand?>(nameof(AddCommand));

    public static readonly StyledProperty<object?> AddCommandParameterProperty =
        AvaloniaProperty.Register<SignalBindingsEditor, object?>(nameof(AddCommandParameter));

    public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
        AvaloniaProperty.Register<SignalBindingsEditor, ICommand?>(nameof(RemoveCommand));

    public static readonly StyledProperty<IEnumerable?> AllowedRolesProperty =
        AvaloniaProperty.Register<SignalBindingsEditor, IEnumerable?>(nameof(AllowedRoles));

    public SignalBindingsEditor()
    {
        InitializeComponent();
    }

    public IEnumerable? Bindings
    {
        get => GetValue(BindingsProperty);
        set => SetValue(BindingsProperty, value);
    }

    public ICommand? AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public object? AddCommandParameter
    {
        get => GetValue(AddCommandParameterProperty);
        set => SetValue(AddCommandParameterProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public IEnumerable AllowedRoles
    {
        get => GetValue(AllowedRolesProperty) ?? SignalBindingRoles;
        set => SetValue(AllowedRolesProperty, value);
    }

    public IReadOnlyList<SignalBindingRole> SignalBindingRoles { get; } = Enum.GetValues<SignalBindingRole>()
        .Where(role => !IsDeprecatedSignalRole(role))
        .ToArray();
    public IReadOnlyList<SignalBindingDirection> SignalBindingDirections { get; } = Enum.GetValues<SignalBindingDirection>();
    public IReadOnlyList<SignalValueType> SignalValueTypes { get; } = Enum.GetValues<SignalValueType>();

    private static bool IsDeprecatedSignalRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.StartOffFeedback or
        SignalBindingRole.StopOffFeedback or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;
}
