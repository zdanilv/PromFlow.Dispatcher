using System.Collections.ObjectModel;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public sealed class RouteMapConfigurationMapper
{
    private readonly RouteMapDefinition _seed;

    public RouteMapConfigurationMapper(RouteMapDefinition seed)
    {
        _seed = seed;
    }

    public RouteMapDefinition SeedDefinition => _seed;

    public RouteMapConfigurationDocument CreateSeedDocument() => ToDocument(_seed);

    public RouteMapConfigurationDocument ToDocument(RouteMapDefinition definition)
    {
        var display = definition.Display ?? new RouteMapDisplaySettings();
        var document = new RouteMapConfigurationDocument
        {
            TopBar = ToConfiguration(definition.TopBar ?? _seed.TopBar ?? CreateDefaultTopBar()),
            Map = new RouteMapSettingsConfiguration
            {
                LogicalWidth = definition.LogicalWidth,
                LogicalHeight = definition.LogicalHeight,
                MapPadding = display.MapPadding,
                CardColumnGap = display.CardColumnGap,
                FragmentLength = display.FragmentLength,
                FragmentGap = display.FragmentGap,
                Palette = ToConfiguration(display.Palette),
            },
            Chains = new ObservableCollection<RouteChainConfiguration>(definition.Chains.Select(chain =>
                new RouteChainConfiguration
                {
                    Id = chain.Id,
                    X = chain.X,
                    Y = chain.Y,
                    NodeIds = new ObservableCollection<string>(chain.NodeIds),
                    SegmentIds = new ObservableCollection<string>(chain.SegmentIds),
                })),
            Nodes = new ObservableCollection<RouteNodeConfiguration>(definition.Nodes.Select(ToConfiguration)),
            Segments = new ObservableCollection<RouteSegmentConfiguration>(definition.Segments.Select(ToConfiguration)),
            Cards = new ObservableCollection<EquipmentCardConfiguration>(definition.MapEquipment.Select(card =>
                ToConfiguration(card, definition.Chains))),
            PlaceholderRules = new ObservableCollection<RoutePlaceholderRuleConfiguration>(
                (definition.PlaceholderRules ?? CreateDefaultPlaceholderRules(definition.MapEquipment))
                .Select(ToConfiguration)),
        };

        return document;
    }

    public RouteMapDefinition ToDefinition(RouteMapConfigurationDocument document)
    {
        var cards = document.Cards.Select(ToModel).ToArray();
        var cardsByChain = cards
            .Where(x => !string.IsNullOrWhiteSpace(x.AttachedChainId))
            .GroupBy(x => x.AttachedChainId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var chains = document.Chains.Select(chain =>
        {
            cardsByChain.TryGetValue(chain.Id, out var card);
            return new RouteChain(
                chain.Id,
                chain.X,
                chain.Y,
                chain.NodeIds.ToArray(),
                chain.SegmentIds.ToArray(),
                card?.Id,
                card?.AttachedCardRightOffset ?? 0,
                card?.VerticalAnchor ?? RouteCardVerticalAnchor.ChainBoundsCenter);
        }).ToArray();

        return new RouteMapDefinition(
            document.Map.LogicalWidth,
            document.Map.LogicalHeight,
            chains,
            document.Nodes.Select(ToModel).ToArray(),
            document.Segments.Select(ToModel).ToArray(),
            _seed.Vehicles,
            cards,
            _seed.Requests,
            _seed.RequestTemplates,
            new RouteMapDisplaySettings
            {
                MapPadding = document.Map.MapPadding,
                CardColumnGap = document.Map.CardColumnGap,
                FragmentLength = document.Map.FragmentLength,
                FragmentGap = document.Map.FragmentGap,
                Palette = ToModel(document.Map.Palette),
            },
            document.PlaceholderRules.Select(ToModel).ToArray(),
            ToModel(document.TopBar));
    }

    private static RouteNodeConfiguration ToConfiguration(RouteNode node)
    {
        var style = node.Style ?? new RouteNodeStyle();
        return new RouteNodeConfiguration
        {
            Id = node.Id,
            Title = node.Title,
            X = node.X,
            Y = node.Y,
            Kind = node.Kind,
            State = node.State,
            LabelOffsetX = node.LabelOffsetX,
            LabelOffsetY = node.LabelOffsetY,
            LabelPlacement = node.LabelPlacement,
            IsLoader = node.IsLoader,
            IsTarget = node.IsTarget,
            MenuKind = node.MenuKind,
            IsVisible = node.IsVisible,
            Style = new RouteNodeStyleConfiguration
            {
                Radius = style.Radius,
                InnerRadiusRatio = style.InnerRadiusRatio,
                BorderThickness = style.BorderThickness,
                FillColor = style.FillColor,
                BorderColor = style.BorderColor,
                InnerColor = style.InnerColor,
                LabelColor = style.LabelColor,
                LabelFontSize = style.LabelFontSize,
                ActiveOutlineColor = style.ActiveOutlineColor,
                ActiveOutlineThickness = style.ActiveOutlineThickness,
            },
            Bindings = new ObservableCollection<SignalBindingConfiguration>(
                node.Bindings
                    .Where(binding => !IsDeprecatedSignalRole(binding.Role))
                    .Select(ToConfiguration)),
        };
    }

    private static RouteNode ToModel(RouteNodeConfiguration node)
    {
        return new RouteNode(
            node.Id,
            node.Title,
            node.X,
            node.Y,
            node.Kind,
            node.State,
            node.Bindings
                .Where(binding => !IsDeprecatedSignalRole(binding.Role))
                .Select(ToModel)
                .ToArray(),
            node.LabelOffsetX,
            node.LabelOffsetY,
            node.LabelPlacement,
            node.IsLoader,
            node.IsTarget,
            node.MenuKind,
            node.IsVisible,
            new RouteNodeStyle
            {
                Radius = node.Style.Radius,
                InnerRadiusRatio = node.Style.InnerRadiusRatio,
                BorderThickness = node.Style.BorderThickness,
                FillColor = node.Style.FillColor,
                BorderColor = node.Style.BorderColor,
                InnerColor = node.Style.InnerColor,
                LabelColor = node.Style.LabelColor,
                LabelFontSize = node.Style.LabelFontSize,
                ActiveOutlineColor = node.Style.ActiveOutlineColor,
                ActiveOutlineThickness = node.Style.ActiveOutlineThickness,
            });
    }

    private static RouteTopBarConfiguration ToConfiguration(RouteTopBarSettings topBar) => new()
    {
        Automatic = ToConfiguration(topBar.Automatic),
        Manual = ToConfiguration(topBar.Manual),
        Reset = ToConfiguration(topBar.Reset),
        Emergency = ToEmergencyConfiguration(topBar.Emergency),
    };

    private static RouteTopBarButtonConfiguration ToConfiguration(RouteTopBarButtonSettings button) => new()
    {
        Text = button.Text,
        Bindings = ToButtonBindings(button),
    };

    private static RouteTopBarEmergencyButtonConfiguration ToEmergencyConfiguration(RouteTopBarButtonSettings button) => new()
    {
        Text = button.Text,
        ButtonKind = RouteCommandButtonKind.Toggle,
        OffFeedbackEnabled = false,
        Bindings = ToButtonBindings(button),
    };

    private static ObservableCollection<SignalBindingConfiguration> ToButtonBindings(RouteTopBarButtonSettings button)
    {
        var bindings = new List<SignalBindingConfiguration> { ToConfiguration(button.Binding) };
        if (button.EnabledBinding is not null)
            bindings.Add(ToConfiguration(button.EnabledBinding));
        return new ObservableCollection<SignalBindingConfiguration>(bindings);
    }

    private static RouteTopBarSettings ToModel(RouteTopBarConfiguration topBar) => new(
        ToModel(
            topBar.Automatic,
            SignalBindingRole.AutomaticModeCommand,
            "system.mode.automatic",
            offFeedbackRole: null,
            offFeedbackSignalId: null,
            offFeedbackEnabled: false,
            RouteCommandButtonKind.Toggle),
        ToModel(
            topBar.Manual,
            SignalBindingRole.ManualModeCommand,
            "system.mode.manual",
            offFeedbackRole: null,
            offFeedbackSignalId: null,
            offFeedbackEnabled: false,
            RouteCommandButtonKind.Toggle),
        ToModel(
            topBar.Reset,
            SignalBindingRole.ResetCommand,
            "system.reset",
            offFeedbackRole: null,
            offFeedbackSignalId: null,
            offFeedbackEnabled: false,
            RouteCommandButtonKind.Toggle),
        ToModel(
            topBar.Emergency,
            SignalBindingRole.EmergencyCommand,
            "system.emergency",
            offFeedbackRole: null,
            offFeedbackSignalId: null,
            offFeedbackEnabled: false,
            RouteCommandButtonKind.Toggle));

    private static RouteTopBarButtonSettings ToModel(
        RouteTopBarButtonConfiguration button,
        SignalBindingRole role,
        string signalId,
        SignalBindingRole? offFeedbackRole,
        string? offFeedbackSignalId,
        bool offFeedbackEnabled,
        RouteCommandButtonKind buttonKind) => new()
        {
            Text = button.Text,
            ButtonKind = buttonKind,
            OffFeedbackEnabled = offFeedbackEnabled,
            Binding = button.Bindings.FirstOrDefault(x => x.Role == role) is { } binding
                ? ToModel(binding)
                : new SignalBinding(role, signalId, SignalBindingDirection.ReadWrite, Configurator.Application.Services.Signals.SignalValueType.Bool),
            OffFeedbackBinding = offFeedbackRole is { } actualOffRole &&
                                 button.Bindings.FirstOrDefault(x => x.Role == actualOffRole) is { } offFeedback
                ? ToModel(offFeedback)
                : offFeedbackEnabled && offFeedbackRole.HasValue && offFeedbackSignalId is not null
                    ? new SignalBinding(offFeedbackRole.Value, offFeedbackSignalId, SignalBindingDirection.Read, Configurator.Application.Services.Signals.SignalValueType.Bool)
                    : null,
            EnabledBinding = button.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.Enabled) is { } enabled
                ? ToModel(enabled)
                : null,
        };

    private static RouteTopBarSettings CreateDefaultTopBar()
    {
        var configuration = RouteTopBarConfiguration.CreateDefault();
        return ToModel(configuration);
    }

    private static RouteSegmentConfiguration ToConfiguration(RouteSegment segment)
    {
        var style = segment.Style ?? new RouteSegmentStyle();
        return new RouteSegmentConfiguration
        {
            Id = segment.Id,
            FromNodeId = segment.FromNodeId,
            ToNodeId = segment.ToNodeId,
            State = segment.State,
            IsDirectional = segment.IsDirectional,
            Kind = segment.Kind,
            ArcRadius = segment.ArcRadius,
            ElbowOrder = segment.ElbowOrder,
            Title = segment.Title,
            LabelOffsetX = segment.LabelOffsetX,
            LabelOffsetY = segment.LabelOffsetY,
            IsVisible = segment.IsVisible,
            Style = new RouteSegmentStyleConfiguration
            {
                NormalColor = style.NormalColor,
                ActiveColor = style.ActiveColor,
                Thickness = style.Thickness,
                ActiveThickness = style.ActiveThickness,
                FragmentLength = style.FragmentLength,
                FragmentGap = style.FragmentGap,
                EndpointGap = style.EndpointGap,
                LineCap = style.LineCap,
                LabelColor = style.LabelColor,
                LabelFontSize = style.LabelFontSize,
            },
            Bindings = new ObservableCollection<SignalBindingConfiguration>(
                segment.Bindings
                    .Where(binding => !IsDeprecatedSignalRole(binding.Role))
                    .Select(ToConfiguration)),
            ActiveFragments = new ObservableCollection<RouteSegmentActiveFragmentConfiguration>(
                (segment.ActiveFragments ?? [])
                    .OrderBy(fragment => fragment.Index)
                    .Select(ToConfiguration)),
        };
    }

    private static RouteSegmentActiveFragmentConfiguration ToConfiguration(RouteSegmentActiveFragment fragment) => new()
    {
        Index = fragment.Index,
        Binding = ToConfiguration(fragment.Binding),
    };

    private static RouteSegment ToModel(RouteSegmentConfiguration segment)
    {
        return new RouteSegment(
            segment.Id,
            segment.FromNodeId,
            segment.ToNodeId,
            segment.State,
            segment.IsDirectional,
            segment.Bindings
                .Where(binding => !IsDeprecatedSignalRole(binding.Role))
                .Select(ToModel)
                .ToArray(),
            segment.Kind,
            segment.ArcRadius,
            segment.ElbowOrder,
            segment.Title,
            segment.LabelOffsetX,
            segment.LabelOffsetY,
            segment.IsVisible,
            new RouteSegmentStyle
            {
                NormalColor = segment.Style.NormalColor,
                ActiveColor = segment.Style.ActiveColor,
                Thickness = segment.Style.Thickness,
                ActiveThickness = segment.Style.ActiveThickness,
                FragmentLength = segment.Style.FragmentLength,
                FragmentGap = segment.Style.FragmentGap,
                EndpointGap = segment.Style.EndpointGap,
                LineCap = segment.Style.LineCap,
                LabelColor = segment.Style.LabelColor,
                LabelFontSize = segment.Style.LabelFontSize,
            },
            segment.ActiveFragments
                .OrderBy(fragment => fragment.Index)
                .Select(ToModel)
                .ToArray());
    }

    private static RouteSegmentActiveFragment ToModel(RouteSegmentActiveFragmentConfiguration fragment) =>
        new(fragment.Index, ToModel(fragment.Binding));

    private static EquipmentCardConfiguration ToConfiguration(
        EquipmentCommandCard card,
        IReadOnlyList<RouteChain> chains)
    {
        var chain = chains.FirstOrDefault(x => x.AttachedEquipmentCardId == card.Id);
        var style = card.Style ?? new EquipmentCardStyle();
        var anchor = card.VerticalAnchor ?? chain?.AttachedCardVerticalAnchor ?? RouteCardVerticalAnchor.ChainBoundsCenter;
        return new EquipmentCardConfiguration
        {
            Id = card.Id,
            Title = card.Title,
            StatusText = card.StatusText,
            State = card.State,
            CanStart = card.CanStart,
            CanStop = card.CanStop,
            StartButtonKind = RouteCommandButtonKind.Toggle,
            StopButtonKind = RouteCommandButtonKind.Toggle,
            StartOffFeedbackEnabled = card.StartOffFeedbackEnabled,
            StopOffFeedbackEnabled = card.StopOffFeedbackEnabled,
            IsVisible = card.IsVisible,
            AttachedChainId = card.AttachedChainId ?? chain?.Id,
            AttachedCardRightOffset = card.AttachedChainId is not null
                ? card.AttachedCardRightOffset
                : chain?.AttachedCardRightOffset ?? 0,
            VerticalAnchorKind = anchor.Kind,
            VerticalAnchorNodeId = anchor.NodeId,
            Style = ToConfiguration(style),
            Bindings = new ObservableCollection<SignalBindingConfiguration>(
                card.Bindings
                    .Where(binding => !IsDeprecatedSignalRole(binding.Role))
                    .Select(ToConfiguration)),
            Parameters = new ObservableCollection<EquipmentCardParameterConfiguration>(
                card.Parameters.Select(ToConfiguration)),
        };
    }

    private static EquipmentCommandCard ToModel(EquipmentCardConfiguration card)
    {
        return new EquipmentCommandCard(
            card.Id,
            card.Title,
            card.StatusText,
            card.State,
            card.CanStart,
            card.CanStop,
            card.Bindings
                .Where(binding => !IsDeprecatedSignalRole(binding.Role))
                .Select(ToModel)
                .ToArray(),
            RouteCommandButtonKind.Toggle,
            RouteCommandButtonKind.Toggle,
            StartOffFeedbackEnabled: card.StartOffFeedbackEnabled,
            StopOffFeedbackEnabled: card.StopOffFeedbackEnabled,
            card.IsVisible,
            card.AttachedChainId,
            card.AttachedCardRightOffset,
            new RouteCardVerticalAnchor(card.VerticalAnchorKind, card.VerticalAnchorNodeId),
            ToModel(card.Style))
        {
            Parameters = card.Parameters.Select(ToModel).ToArray(),
        };
    }

    private static EquipmentCardParameterConfiguration ToConfiguration(EquipmentCardParameter parameter) => new()
    {
        Title = parameter.Title,
        Role = parameter.Binding.Role,
        SignalId = parameter.Binding.SignalId,
        Direction = parameter.Binding.Direction,
        ValueType = parameter.Binding.ValueType,
    };

    private static EquipmentCardParameter ToModel(EquipmentCardParameterConfiguration parameter) => new(
        parameter.Title,
        new SignalBinding(
            SignalBindingRole.EquipmentParameter,
            parameter.SignalId,
            parameter.Direction,
            parameter.ValueType));

    private static EquipmentCardStyleConfiguration ToConfiguration(EquipmentCardStyle style)
    {
        return new EquipmentCardStyleConfiguration
        {
            Width = style.Width,
            MinimumWidth = style.MinimumWidth,
            Height = style.Height,
            Margin = ToConfiguration(style.Margin),
            Padding = ToConfiguration(style.Padding),
            BackgroundColor = style.BackgroundColor,
            BorderColor = style.BorderColor,
            BorderThickness = ToConfiguration(style.BorderThickness),
            CornerRadius = ToConfiguration(style.CornerRadius),
            TitleColor = style.TitleColor,
            TextColor = style.TextColor,
            TitleFontSize = style.TitleFontSize,
            StatusFontSize = style.StatusFontSize,
            RouteTextFontSize = style.RouteTextFontSize,
            ActionFontSize = style.ActionFontSize,
            StartText = style.StartText,
            StopText = style.StopText,
            SendPrefix = style.SendPrefix,
            ReturnPrefix = style.ReturnPrefix,
        };
    }

    private static EquipmentCardStyle ToModel(EquipmentCardStyleConfiguration style)
    {
        return new EquipmentCardStyle
        {
            Width = style.Width,
            MinimumWidth = style.MinimumWidth,
            Height = style.Height,
            Margin = ToModel(style.Margin),
            Padding = ToModel(style.Padding),
            BackgroundColor = style.BackgroundColor,
            BorderColor = style.BorderColor,
            BorderThickness = ToModel(style.BorderThickness),
            CornerRadius = ToModel(style.CornerRadius),
            TitleColor = style.TitleColor,
            TextColor = style.TextColor,
            TitleFontSize = style.TitleFontSize,
            StatusFontSize = style.StatusFontSize,
            RouteTextFontSize = style.RouteTextFontSize,
            ActionFontSize = style.ActionFontSize,
            StartText = style.StartText,
            StopText = style.StopText,
            SendPrefix = style.SendPrefix,
            ReturnPrefix = style.ReturnPrefix,
        };
    }

    private static RoutePlaceholderRuleConfiguration ToConfiguration(RoutePlaceholderRule rule)
    {
        var style = rule.Style ?? new RoutePlaceholderStyle();
        return new RoutePlaceholderRuleConfiguration
        {
            Id = rule.Id,
            CardId = rule.CardId,
            Placement = rule.Placement,
            HeightMode = rule.HeightMode,
            FixedHeight = rule.FixedHeight,
            Gap = rule.Gap,
            MaximumCount = rule.MaximumCount,
            IsVisible = rule.IsVisible,
            Style = new RoutePlaceholderStyleConfiguration
            {
                BackgroundColor = style.BackgroundColor,
                BorderColor = style.BorderColor,
                BorderThickness = ToConfiguration(style.BorderThickness),
                CornerRadius = ToConfiguration(style.CornerRadius),
                Margin = ToConfiguration(style.Margin),
            },
        };
    }

    private static RoutePlaceholderRule ToModel(RoutePlaceholderRuleConfiguration rule)
    {
        return new RoutePlaceholderRule(
            rule.Id,
            rule.CardId,
            rule.Placement,
            rule.HeightMode,
            rule.FixedHeight,
            rule.Gap,
            rule.MaximumCount,
            rule.IsVisible,
            new RoutePlaceholderStyle
            {
                BackgroundColor = rule.Style.BackgroundColor,
                BorderColor = rule.Style.BorderColor,
                BorderThickness = ToModel(rule.Style.BorderThickness),
                CornerRadius = ToModel(rule.Style.CornerRadius),
                Margin = ToModel(rule.Style.Margin),
            });
    }

    private static IEnumerable<RoutePlaceholderRule> CreateDefaultPlaceholderRules(
        IEnumerable<EquipmentCommandCard> cards)
    {
        return cards.Select(card => new RoutePlaceholderRule(
            $"placeholder.{card.Id}",
            card.Id,
            RoutePlaceholderPlacement.Both,
            RoutePlaceholderHeightMode.MatchCard,
            FixedHeight: 141,
            Gap: 10,
            MaximumCount: null,
            IsVisible: true));
    }

    private static SignalBindingConfiguration ToConfiguration(SignalBinding binding) => new()
    {
        Role = binding.Role,
        SignalId = binding.SignalId,
        Direction = binding.Direction,
        ValueType = binding.ValueType,
    };

    private static SignalBinding ToModel(SignalBindingConfiguration binding) =>
        new(binding.Role, binding.SignalId, binding.Direction, binding.ValueType);

    private static bool IsDeprecatedSignalRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;

    private static RouteMapPaletteConfiguration ToConfiguration(RouteMapPaletteSettings palette) => new()
    {
        Background = palette.Background,
        Text = palette.Text,
        MutedText = palette.MutedText,
        Track = palette.Track,
        ActiveTrack = palette.ActiveTrack,
        Ready = palette.Ready,
        Running = palette.Running,
        Warning = palette.Warning,
        Fault = palette.Fault,
        Offline = palette.Offline,
        Disabled = palette.Disabled,
        NodeFill = palette.NodeFill,
        Selection = palette.Selection,
        Hover = palette.Hover,
    };

    private static RouteMapPaletteSettings ToModel(RouteMapPaletteConfiguration palette) => new()
    {
        Background = palette.Background,
        Text = palette.Text,
        MutedText = palette.MutedText,
        Track = palette.Track,
        ActiveTrack = palette.ActiveTrack,
        Ready = palette.Ready,
        Running = palette.Running,
        Warning = palette.Warning,
        Fault = palette.Fault,
        Offline = palette.Offline,
        Disabled = palette.Disabled,
        NodeFill = palette.NodeFill,
        Selection = palette.Selection,
        Hover = palette.Hover,
    };

    private static RouteThicknessConfiguration ToConfiguration(RouteThickness value) => new()
    {
        Left = value.Left,
        Top = value.Top,
        Right = value.Right,
        Bottom = value.Bottom,
    };

    private static RouteThickness ToModel(RouteThicknessConfiguration value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);

    private static RouteCornerRadiusConfiguration ToConfiguration(RouteCornerRadius value) => new()
    {
        TopLeft = value.TopLeft,
        TopRight = value.TopRight,
        BottomRight = value.BottomRight,
        BottomLeft = value.BottomLeft,
    };

    private static RouteCornerRadius ToModel(RouteCornerRadiusConfiguration value) =>
        new(value.TopLeft, value.TopRight, value.BottomRight, value.BottomLeft);
}
