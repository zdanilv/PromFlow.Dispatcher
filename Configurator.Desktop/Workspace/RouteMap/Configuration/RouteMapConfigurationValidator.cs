using System.Globalization;
using System.Text.RegularExpressions;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Configuration;

public sealed record RouteMapConfigurationError(
    string Scope,
    string? ObjectId,
    string Property,
    string Message);

public sealed record RouteMapConfigurationValidationResult(
    IReadOnlyList<RouteMapConfigurationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class RouteMapConfigurationValidator
{
    private static readonly Regex ColorPattern = new(
        "^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public RouteMapConfigurationValidationResult Validate(RouteMapConfigurationDocument? document)
    {
        var errors = new List<RouteMapConfigurationError>();
        if (document is null)
        {
            errors.Add(new("document", null, string.Empty, "Документ настроек отсутствует."));
            return new RouteMapConfigurationValidationResult(errors);
        }

        if (document.SchemaVersion != RouteMapConfigurationDocument.CurrentSchemaVersion)
            Add(errors, "document", null, nameof(document.SchemaVersion),
                $"Поддерживается версия {RouteMapConfigurationDocument.CurrentSchemaVersion}, получена {document.SchemaVersion}.");

        ValidatePositive(errors, "map", null, nameof(document.Map.LogicalWidth), document.Map.LogicalWidth);
        ValidatePositive(errors, "map", null, nameof(document.Map.LogicalHeight), document.Map.LogicalHeight);
        ValidateNonNegative(errors, "map", null, nameof(document.Map.MapPadding), document.Map.MapPadding);
        ValidateNonNegative(errors, "map", null, nameof(document.Map.CardColumnGap), document.Map.CardColumnGap);
        ValidatePositive(errors, "map", null, nameof(document.Map.FragmentLength), document.Map.FragmentLength);
        ValidateNonNegative(errors, "map", null, nameof(document.Map.FragmentGap), document.Map.FragmentGap);
        ValidatePalette(errors, document.Map.Palette);
        ValidateTopBar(errors, document.TopBar);

        ValidateIds(errors, "chain", document.Chains);
        ValidateIds(errors, "node", document.Nodes);
        ValidateIds(errors, "segment", document.Segments);
        ValidateIds(errors, "card", document.Cards);
        ValidateIds(errors, "placeholder", document.PlaceholderRules);

        var runtimeIds = document.Nodes.Select(x => (Scope: "node", Item: (RouteMapConfigurationItem)x))
            .Concat(document.Segments.Select(x => (Scope: "segment", Item: (RouteMapConfigurationItem)x)))
            .Concat(document.Cards.Select(x => (Scope: "card", Item: (RouteMapConfigurationItem)x)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Item.Id))
            .GroupBy(x => x.Item.Id, StringComparer.Ordinal);
        foreach (var duplicate in runtimeIds.Where(x => x.Count() > 1))
        {
            foreach (var entry in duplicate)
                Add(errors, entry.Scope, entry.Item.Id, nameof(entry.Item.Id), "ID runtime-объекта должен быть глобально уникальным.");
        }

        var nodes = ToUniqueDictionary(document.Nodes);
        var segments = ToUniqueDictionary(document.Segments);
        var chains = ToUniqueDictionary(document.Chains);
        var cards = ToUniqueDictionary(document.Cards);

        foreach (var node in document.Nodes)
        {
            ValidateFinite(errors, "node", node.Id, nameof(node.X), node.X);
            ValidateFinite(errors, "node", node.Id, nameof(node.Y), node.Y);
            ValidateNonNegative(errors, "node", node.Id, nameof(node.Style.Radius), node.Style.Radius);
            if (!IsFinite(node.Style.InnerRadiusRatio) || node.Style.InnerRadiusRatio is < 0 or > 1)
                Add(errors, "node", node.Id, nameof(node.Style.InnerRadiusRatio), "Коэффициент внутреннего радиуса должен быть от 0 до 1.");
            ValidateNonNegative(errors, "node", node.Id, nameof(node.Style.BorderThickness), node.Style.BorderThickness);
            ValidateNonNegative(errors, "node", node.Id, nameof(node.Style.ActiveOutlineThickness), node.Style.ActiveOutlineThickness);
            ValidatePositive(errors, "node", node.Id, nameof(node.Style.LabelFontSize), node.Style.LabelFontSize);
            ValidateColors(errors, "node", node.Id,
                (nameof(node.Style.FillColor), node.Style.FillColor),
                (nameof(node.Style.BorderColor), node.Style.BorderColor),
                (nameof(node.Style.InnerColor), node.Style.InnerColor),
                (nameof(node.Style.LabelColor), node.Style.LabelColor),
                (nameof(node.Style.ActiveOutlineColor), node.Style.ActiveOutlineColor));
            ValidateBindings(errors, "node", node.Id, node.Bindings,
                [SignalBindingRole.State, SignalBindingRole.Visible, SignalBindingRole.Fault, SignalBindingRole.ActiveRoute,
                    SignalBindingRole.TargetCommand,
                    SignalBindingRole.LoaderCommand]);
            ValidateRequiredBinding(errors, "node", node.Id, node.Bindings, SignalBindingRole.ActiveRoute, SignalBindingDirection.Read);
            if (node.MenuKind is RouteNodeMenuKind.SendOnly or RouteNodeMenuKind.SendAndReturn)
            {
                ValidateRequiredBinding(errors, "node", node.Id, node.Bindings, SignalBindingRole.TargetCommand, SignalBindingDirection.ReadWrite);
            }
            if (node.MenuKind == RouteNodeMenuKind.SendAndReturn)
            {
                ValidateRequiredBinding(errors, "node", node.Id, node.Bindings, SignalBindingRole.LoaderCommand, SignalBindingDirection.ReadWrite);
            }
        }

        foreach (var segment in document.Segments)
        {
            if (!nodes.ContainsKey(segment.FromNodeId))
                Add(errors, "segment", segment.Id, nameof(segment.FromNodeId), "Начальный узел не существует.");
            if (!nodes.ContainsKey(segment.ToNodeId))
                Add(errors, "segment", segment.Id, nameof(segment.ToNodeId), "Конечный узел не существует.");
            if (segment.FromNodeId == segment.ToNodeId)
                Add(errors, "segment", segment.Id, nameof(segment.ToNodeId), "Линия должна соединять разные узлы.");

            ValidateNonNegative(errors, "segment", segment.Id, nameof(segment.ArcRadius), segment.ArcRadius);
            ValidatePositive(errors, "segment", segment.Id, nameof(segment.Style.Thickness), segment.Style.Thickness);
            ValidatePositive(errors, "segment", segment.Id, nameof(segment.Style.ActiveThickness), segment.Style.ActiveThickness);
            ValidateNullablePositive(errors, "segment", segment.Id, nameof(segment.Style.FragmentLength), segment.Style.FragmentLength);
            ValidateNullableNonNegative(errors, "segment", segment.Id, nameof(segment.Style.FragmentGap), segment.Style.FragmentGap);
            ValidateNonNegative(errors, "segment", segment.Id, nameof(segment.Style.EndpointGap), segment.Style.EndpointGap);
            ValidatePositive(errors, "segment", segment.Id, nameof(segment.Style.LabelFontSize), segment.Style.LabelFontSize);
            ValidateColors(errors, "segment", segment.Id,
                (nameof(segment.Style.NormalColor), segment.Style.NormalColor),
                (nameof(segment.Style.ActiveColor), segment.Style.ActiveColor),
                (nameof(segment.Style.LabelColor), segment.Style.LabelColor));
            ValidateBindings(errors, "segment", segment.Id, segment.Bindings,
                [SignalBindingRole.State, SignalBindingRole.Visible, SignalBindingRole.Fault, SignalBindingRole.ActiveRoute]);
            var activeRouteBindings = segment.Bindings.Where(x => x.Role == SignalBindingRole.ActiveRoute).ToArray();
            if (activeRouteBindings.Length != 1)
                Add(errors, "segment", segment.Id, nameof(segment.Bindings), "Линия должна иметь ровно один binding ActiveRoute.");
            else
            {
                var activeRoute = activeRouteBindings[0];
                if (activeRoute.Direction != SignalBindingDirection.Read)
                    Add(errors, "segment", segment.Id, nameof(activeRoute.Direction), "ActiveRoute должен иметь направление Read.");
                if (activeRoute.ValueType != Configurator.Application.Services.Signals.SignalValueType.Bool)
                    Add(errors, "segment", segment.Id, nameof(activeRoute.ValueType), "ActiveRoute должен иметь тип Bool.");
            }

            if (segment.Kind == RouteSegmentKind.RoundedElbow90 &&
                nodes.TryGetValue(segment.FromNodeId, out var from) &&
                nodes.TryGetValue(segment.ToNodeId, out var to))
            {
                var maximumRadius = Math.Min(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
                if (segment.ArcRadius > maximumRadius)
                    Add(errors, "segment", segment.Id, nameof(segment.ArcRadius),
                        $"Радиус дуги не должен превышать {maximumRadius.ToString(CultureInfo.InvariantCulture)}.");
            }
        }

        foreach (var chain in document.Chains)
        {
            ValidateFinite(errors, "chain", chain.Id, nameof(chain.X), chain.X);
            ValidateFinite(errors, "chain", chain.Id, nameof(chain.Y), chain.Y);
            foreach (var nodeId in chain.NodeIds.Distinct(StringComparer.Ordinal))
            {
                if (!nodes.ContainsKey(nodeId))
                    Add(errors, "chain", chain.Id, nameof(chain.NodeIds), $"Узел '{nodeId}' не существует.");
            }
            foreach (var segmentId in chain.SegmentIds.Distinct(StringComparer.Ordinal))
            {
                if (!segments.ContainsKey(segmentId))
                    Add(errors, "chain", chain.Id, nameof(chain.SegmentIds), $"Линия '{segmentId}' не существует.");
            }
        }

        foreach (var card in document.Cards)
        {
            if (!string.IsNullOrWhiteSpace(card.AttachedChainId) && !chains.ContainsKey(card.AttachedChainId))
                Add(errors, "card", card.Id, nameof(card.AttachedChainId), "Цепочка карточки не существует.");
            if (card.VerticalAnchorKind == RouteCardVerticalAnchorKind.Node)
            {
                if (string.IsNullOrWhiteSpace(card.VerticalAnchorNodeId) || !nodes.ContainsKey(card.VerticalAnchorNodeId))
                    Add(errors, "card", card.Id, nameof(card.VerticalAnchorNodeId), "Узел вертикального якоря не существует.");
                else if (!string.IsNullOrWhiteSpace(card.AttachedChainId) &&
                         chains.TryGetValue(card.AttachedChainId, out var chain) &&
                         !chain.NodeIds.Contains(card.VerticalAnchorNodeId))
                    Add(errors, "card", card.Id, nameof(card.VerticalAnchorNodeId), "Узел якоря не входит в выбранную цепочку.");
            }

            ValidateNonNegative(errors, "card", card.Id, nameof(card.AttachedCardRightOffset), card.AttachedCardRightOffset);
            ValidatePositive(errors, "card", card.Id, nameof(card.Style.Width), card.Style.Width);
            ValidatePositive(errors, "card", card.Id, nameof(card.Style.MinimumWidth), card.Style.MinimumWidth);
            ValidatePositive(errors, "card", card.Id, nameof(card.Style.Height), card.Style.Height);
            ValidatePositive(errors, "card", card.Id, nameof(card.Style.TitleFontSize), card.Style.TitleFontSize);
            ValidatePositive(errors, "card", card.Id, nameof(card.Style.StatusFontSize), card.Style.StatusFontSize);
            ValidatePositive(errors, "card", card.Id, nameof(card.Style.RouteTextFontSize), card.Style.RouteTextFontSize);
            ValidatePositive(errors, "card", card.Id, nameof(card.Style.ActionFontSize), card.Style.ActionFontSize);
            if (card.Style.MinimumWidth > card.Style.Width)
                Add(errors, "card", card.Id, nameof(card.Style.MinimumWidth), "Минимальная ширина не должна превышать ширину.");
            ValidateEnum(errors, "card", card.Id, nameof(card.StartButtonKind), card.StartButtonKind);
            ValidateEnum(errors, "card", card.Id, nameof(card.StopButtonKind), card.StopButtonKind);
            ValidateThickness(errors, "card", card.Id, nameof(card.Style.Margin), card.Style.Margin);
            ValidateThickness(errors, "card", card.Id, nameof(card.Style.Padding), card.Style.Padding);
            ValidateThickness(errors, "card", card.Id, nameof(card.Style.BorderThickness), card.Style.BorderThickness);
            ValidateCornerRadius(errors, "card", card.Id, nameof(card.Style.CornerRadius), card.Style.CornerRadius);
            ValidateColors(errors, "card", card.Id,
                (nameof(card.Style.BackgroundColor), card.Style.BackgroundColor),
                (nameof(card.Style.BorderColor), card.Style.BorderColor),
                (nameof(card.Style.TitleColor), card.Style.TitleColor),
                (nameof(card.Style.TextColor), card.Style.TextColor),
                (nameof(card.Style.StartColor), card.Style.StartColor),
                (nameof(card.Style.StartCheckedColor), card.Style.StartCheckedColor),
                (nameof(card.Style.StopColor), card.Style.StopColor),
                (nameof(card.Style.StopCheckedColor), card.Style.StopCheckedColor));
            var cardAllowedRoles = new List<SignalBindingRole>
            {
                SignalBindingRole.State,
                SignalBindingRole.Text,
                SignalBindingRole.Value,
                SignalBindingRole.Visible,
                SignalBindingRole.StartCommand,
                SignalBindingRole.StopCommand,
                SignalBindingRole.Fault,
            };
            if (card.StartButtonKind == RouteCommandButtonKind.Toggle && card.StartOffFeedbackEnabled)
                cardAllowedRoles.Add(SignalBindingRole.StartOffFeedback);
            if (card.StopButtonKind == RouteCommandButtonKind.Toggle && card.StopOffFeedbackEnabled)
                cardAllowedRoles.Add(SignalBindingRole.StopOffFeedback);
            ValidateBindings(errors, "card", card.Id, card.Bindings, cardAllowedRoles);
            if (card.CanStart)
            {
                ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.StartCommand, SignalBindingDirection.ReadWrite);
                if (card.StartButtonKind == RouteCommandButtonKind.Toggle && card.StartOffFeedbackEnabled)
                    ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.StartOffFeedback, SignalBindingDirection.Read);
            }
            if (card.CanStop)
            {
                ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.StopCommand, SignalBindingDirection.ReadWrite);
                if (card.StopButtonKind == RouteCommandButtonKind.Toggle && card.StopOffFeedbackEnabled)
                    ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.StopOffFeedback, SignalBindingDirection.Read);
            }
        }

        foreach (var duplicatedChain in document.Cards
                     .Where(x => !string.IsNullOrWhiteSpace(x.AttachedChainId))
                     .GroupBy(x => x.AttachedChainId!, StringComparer.Ordinal)
                     .Where(x => x.Count() > 1))
        {
            foreach (var card in duplicatedChain)
                Add(errors, "card", card.Id, nameof(card.AttachedChainId), "К одной цепочке можно привязать только одну карточку.");
        }

        foreach (var rule in document.PlaceholderRules)
        {
            if (!cards.ContainsKey(rule.CardId))
                Add(errors, "placeholder", rule.Id, nameof(rule.CardId), "Карточка заглушки не существует.");
            ValidateNonNegative(errors, "placeholder", rule.Id, nameof(rule.Gap), rule.Gap);
            if (rule.HeightMode == RoutePlaceholderHeightMode.Fixed)
                ValidatePositive(errors, "placeholder", rule.Id, nameof(rule.FixedHeight), rule.FixedHeight);
            if (rule.MaximumCount is <= 0)
                Add(errors, "placeholder", rule.Id, nameof(rule.MaximumCount), "Максимальное количество должно быть больше нуля.");
            ValidateThickness(errors, "placeholder", rule.Id, nameof(rule.Style.Margin), rule.Style.Margin);
            ValidateThickness(errors, "placeholder", rule.Id, nameof(rule.Style.BorderThickness), rule.Style.BorderThickness);
            ValidateCornerRadius(errors, "placeholder", rule.Id, nameof(rule.Style.CornerRadius), rule.Style.CornerRadius);
            ValidateColors(errors, "placeholder", rule.Id,
                (nameof(rule.Style.BackgroundColor), rule.Style.BackgroundColor),
                (nameof(rule.Style.BorderColor), rule.Style.BorderColor));
        }

        if (document.Nodes.Count(x => x.IsLoader) > 1)
            Add(errors, "node", null, nameof(RouteNodeConfiguration.IsLoader), "Начальная роль IsLoader должна быть уникальной.");
        if (document.Nodes.Count(x => x.IsTarget) > 1)
            Add(errors, "node", null, nameof(RouteNodeConfiguration.IsTarget), "Начальная роль IsTarget должна быть уникальной.");
        foreach (var node in document.Nodes.Where(x => x.IsLoader && x.IsTarget))
            Add(errors, "node", node.Id, nameof(node.IsTarget), "Один узел не может одновременно быть loader и target.");

        return new RouteMapConfigurationValidationResult(errors);
    }

    private static void ValidateBindings(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string objectId,
        IEnumerable<SignalBindingConfiguration> bindings,
        IReadOnlyCollection<SignalBindingRole> allowedRoles)
    {
        var bindingArray = bindings.ToArray();
        foreach (var duplicate in bindingArray.GroupBy(x => x.Role).Where(x => x.Count() > 1))
            Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Role), $"Роль {duplicate.Key} указана несколько раз.");

        foreach (var binding in bindingArray)
        {
            if (!allowedRoles.Contains(binding.Role))
                Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Role), $"Роль {binding.Role} недоступна для этого объекта.");
            if (string.IsNullOrWhiteSpace(binding.SignalId))
                Add(errors, scope, objectId, nameof(SignalBindingConfiguration.SignalId), "SignalId обязателен.");

            var isCommand = IsCommandRole(binding.Role);
            if (IsOffFeedbackRole(binding.Role))
            {
                if (binding.Direction != SignalBindingDirection.Read)
                    Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Direction), $"Binding {binding.Role} must use Read direction.");
                if (binding.ValueType != Configurator.Application.Services.Signals.SignalValueType.Bool)
                    Add(errors, scope, objectId, nameof(SignalBindingConfiguration.ValueType), $"Binding {binding.Role} must use Bool value type.");
            }
            if (isCommand && binding.Direction == SignalBindingDirection.Read)
                Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Direction), "Команда должна иметь направление Write или ReadWrite.");
            if (!isCommand && binding.Direction == SignalBindingDirection.Write)
                Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Direction), "Состояние должно иметь направление Read или ReadWrite.");
        }
    }

    private static void ValidateTopBar(
        ICollection<RouteMapConfigurationError> errors,
        RouteTopBarConfiguration topBar)
    {
        ValidateTopBarButton(
            errors,
            "automatic",
            topBar.Automatic,
            SignalBindingRole.AutomaticModeCommand,
            offFeedbackRole: null,
            requireOffFeedback: false);
        ValidateTopBarButton(
            errors,
            "manual",
            topBar.Manual,
            SignalBindingRole.ManualModeCommand,
            offFeedbackRole: null,
            requireOffFeedback: false);
        ValidateTopBarButton(
            errors,
            "emergency",
            topBar.Emergency,
            SignalBindingRole.EmergencyCommand,
            topBar.Emergency.ButtonKind == RouteCommandButtonKind.Toggle && topBar.Emergency.OffFeedbackEnabled
                ? SignalBindingRole.EmergencyOffFeedback
                : null,
            requireOffFeedback: topBar.Emergency.ButtonKind == RouteCommandButtonKind.Toggle && topBar.Emergency.OffFeedbackEnabled);
    }

    private static void ValidateTopBarButton(
        ICollection<RouteMapConfigurationError> errors,
        string id,
        RouteTopBarButtonConfiguration button,
        SignalBindingRole role,
        SignalBindingRole? offFeedbackRole,
        bool requireOffFeedback)
    {
        if (string.IsNullOrWhiteSpace(button.Text))
            Add(errors, "topBar", id, nameof(button.Text), "Текст кнопки обязателен.");
        ValidateColors(errors, "topBar", id,
            (nameof(button.NormalBackground), button.NormalBackground),
            (nameof(button.CheckedBackground), button.CheckedBackground),
            (nameof(button.NormalForeground), button.NormalForeground),
            (nameof(button.CheckedForeground), button.CheckedForeground));
        if (button is RouteTopBarEmergencyButtonConfiguration emergency)
            ValidateEnum(errors, "topBar", id, nameof(emergency.ButtonKind), emergency.ButtonKind);
        var allowedRoles = offFeedbackRole.HasValue ? [role, offFeedbackRole.Value] : new[] { role };
        ValidateBindings(errors, "topBar", id, button.Bindings, allowedRoles);
        ValidateRequiredBinding(errors, "topBar", id, button.Bindings, role, SignalBindingDirection.ReadWrite);
        if (requireOffFeedback && offFeedbackRole.HasValue)
            ValidateRequiredBinding(errors, "topBar", id, button.Bindings, offFeedbackRole.Value, SignalBindingDirection.Read);
    }

    private static bool IsCommandRole(SignalBindingRole role) => role is
        SignalBindingRole.StartCommand or
        SignalBindingRole.StopCommand or
        SignalBindingRole.TargetCommand or
        SignalBindingRole.LoaderCommand or
        SignalBindingRole.AutomaticModeCommand or
        SignalBindingRole.ManualModeCommand or
        SignalBindingRole.EmergencyCommand;

    private static bool IsOffFeedbackRole(SignalBindingRole role) => role is
        SignalBindingRole.StartOffFeedback or
        SignalBindingRole.StopOffFeedback or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;

    private static void ValidateEnum<TEnum>(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string objectId,
        string property,
        TEnum value) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
            Add(errors, scope, objectId, property, $"Недопустимое значение {typeof(TEnum).Name}: {value}.");
    }

    private static void ValidateRequiredBinding(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string objectId,
        IEnumerable<SignalBindingConfiguration> bindings,
        SignalBindingRole role,
        SignalBindingDirection direction)
    {
        var matches = bindings.Where(x => x.Role == role).ToArray();
        if (matches.Length != 1)
        {
            Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Role), $"Требуется ровно один binding {role}.");
            return;
        }

        if (matches[0].Direction != direction)
            Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Direction), $"Binding {role} должен иметь направление {direction}.");
        if (matches[0].ValueType != Configurator.Application.Services.Signals.SignalValueType.Bool)
            Add(errors, scope, objectId, nameof(SignalBindingConfiguration.ValueType), $"Binding {role} должен иметь тип Bool.");
    }

    private static void ValidateIds<T>(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        IEnumerable<T> items) where T : RouteMapConfigurationItem
    {
        var array = items.ToArray();
        foreach (var item in array.Where(x => string.IsNullOrWhiteSpace(x.Id)))
            Add(errors, scope, item.Id, nameof(item.Id), "ID обязателен.");
        foreach (var duplicate in array.Where(x => !string.IsNullOrWhiteSpace(x.Id))
                     .GroupBy(x => x.Id, StringComparer.Ordinal).Where(x => x.Count() > 1))
            foreach (var item in duplicate)
                Add(errors, scope, item.Id, nameof(item.Id), "ID должен быть уникальным.");
    }

    private static void ValidatePalette(ICollection<RouteMapConfigurationError> errors, RouteMapPaletteConfiguration palette)
    {
        ValidateColors(errors, "map", null,
            (nameof(palette.Background), palette.Background),
            (nameof(palette.Text), palette.Text),
            (nameof(palette.MutedText), palette.MutedText),
            (nameof(palette.Track), palette.Track),
            (nameof(palette.ActiveTrack), palette.ActiveTrack),
            (nameof(palette.Ready), palette.Ready),
            (nameof(palette.Running), palette.Running),
            (nameof(palette.Warning), palette.Warning),
            (nameof(palette.Fault), palette.Fault),
            (nameof(palette.Offline), palette.Offline),
            (nameof(palette.Disabled), palette.Disabled),
            (nameof(palette.NodeFill), palette.NodeFill),
            (nameof(palette.Selection), palette.Selection),
            (nameof(palette.Hover), palette.Hover));
    }

    private static void ValidateColors(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string? objectId,
        params (string Property, string Value)[] colors)
    {
        foreach (var color in colors.Where(x => !ColorPattern.IsMatch(x.Value ?? string.Empty)))
            Add(errors, scope, objectId, color.Property, "Цвет должен иметь формат #RRGGBB или #AARRGGBB.");
    }

    private static void ValidateThickness(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string objectId,
        string property,
        RouteThicknessConfiguration value)
    {
        if (new[] { value.Left, value.Top, value.Right, value.Bottom }.Any(x => !IsFinite(x) || x < 0))
            Add(errors, scope, objectId, property, "Толщины и отступы должны быть конечными неотрицательными числами.");
    }

    private static void ValidateCornerRadius(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string objectId,
        string property,
        RouteCornerRadiusConfiguration value)
    {
        if (new[] { value.TopLeft, value.TopRight, value.BottomRight, value.BottomLeft }.Any(x => !IsFinite(x) || x < 0))
            Add(errors, scope, objectId, property, "Радиусы углов должны быть конечными неотрицательными числами.");
    }

    private static void ValidatePositive(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string? objectId,
        string property,
        double value)
    {
        if (!IsFinite(value) || value <= 0)
            Add(errors, scope, objectId, property, "Значение должно быть конечным и больше нуля.");
    }

    private static void ValidateNonNegative(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string? objectId,
        string property,
        double value)
    {
        if (!IsFinite(value) || value < 0)
            Add(errors, scope, objectId, property, "Значение должно быть конечным и неотрицательным.");
    }

    private static void ValidateFinite(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string? objectId,
        string property,
        double value)
    {
        if (!IsFinite(value))
            Add(errors, scope, objectId, property, "Значение должно быть конечным числом.");
    }

    private static void ValidateNullablePositive(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string objectId,
        string property,
        double? value)
    {
        if (value.HasValue)
            ValidatePositive(errors, scope, objectId, property, value.Value);
    }

    private static void ValidateNullableNonNegative(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string objectId,
        string property,
        double? value)
    {
        if (value.HasValue)
            ValidateNonNegative(errors, scope, objectId, property, value.Value);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static Dictionary<string, T> ToUniqueDictionary<T>(IEnumerable<T> items)
        where T : RouteMapConfigurationItem => items
        .Where(x => !string.IsNullOrWhiteSpace(x.Id))
        .GroupBy(x => x.Id, StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

    private static void Add(
        ICollection<RouteMapConfigurationError> errors,
        string scope,
        string? objectId,
        string property,
        string message) => errors.Add(new RouteMapConfigurationError(scope, objectId, property, message));
}
