using System.Globalization;
using System.Text.RegularExpressions;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
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
        if (document.Map.FrequencyBinding is { } frequency)
        {
            if (frequency.Role != SignalBindingRole.FrequencyFeedback)
                Add(errors, "map", "frequencyBinding", nameof(frequency.Role), "Привязка частоты должна иметь роль FrequencyFeedback.");
            if (string.IsNullOrWhiteSpace(frequency.SignalId))
                Add(errors, "map", "frequencyBinding", nameof(frequency.SignalId), "SignalId частоты обязателен.");
            if (frequency.Direction != SignalBindingDirection.Read)
                Add(errors, "map", "frequencyBinding", nameof(frequency.Direction), "Привязка частоты должна иметь направление Read.");
            if (!RouteMapDisplaySettings.FrequencyValueTypes.Contains(frequency.ValueType))
                Add(errors, "map", "frequencyBinding", nameof(frequency.ValueType), "Привязка частоты должна иметь числовой тип.");
        }
        ValidatePalette(errors, document.Map.Palette);
        ValidateTopBar(errors, document);

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
            if (node.Kind == RouteNodeKind.TerminalAnchor)
            {
                ValidateTerminalAnchor(errors, document, node);
                continue;
            }

            ValidateBindings(errors, "node", node.Id, node.Bindings,
                [SignalBindingRole.Visible, SignalBindingRole.Fault, SignalBindingRole.ActiveRoute,
                    SignalBindingRole.Enabled,
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
            if (!nodes.TryGetValue(segment.FromNodeId ?? string.Empty, out var from))
                Add(errors, "segment", segment.Id, nameof(segment.FromNodeId), "Начальный узел не существует.");
            if (!nodes.TryGetValue(segment.ToNodeId ?? string.Empty, out var to))
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
                [SignalBindingRole.Visible, SignalBindingRole.Fault, SignalBindingRole.ActiveRoute,
                    SignalBindingRole.Enabled]);
            var isDeadEnd = segment.Kind == RouteSegmentKind.DeadEnd;
            var activeRouteBindings = segment.Bindings.Where(x => x.Role == SignalBindingRole.ActiveRoute).ToArray();
            if (activeRouteBindings.Length > 1 || (activeRouteBindings.Length == 0 && !isDeadEnd))
                Add(errors, "segment", segment.Id, nameof(segment.Bindings), "Линия должна иметь ровно один binding ActiveRoute.");
            else if (activeRouteBindings.Length == 1)
            {
                var activeRoute = activeRouteBindings[0];
                if (activeRoute.Direction != SignalBindingDirection.Read)
                    Add(errors, "segment", segment.Id, nameof(activeRoute.Direction), "ActiveRoute должен иметь направление Read.");
                if (activeRoute.ValueType != Configurator.Application.Services.Signals.SignalValueType.Bool)
                    Add(errors, "segment", segment.Id, nameof(activeRoute.ValueType), "ActiveRoute должен иметь тип Bool.");
            }
            ValidateTerminalSegment(errors, segment, from, to);
            ValidateSegmentActiveFragments(errors, document, segment);

            if (segment.Kind == RouteSegmentKind.RoundedElbow90 && from is not null && to is not null)
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
            var usesPowerFeedback = card.Id == "equip.bucket" || card.StartButtonKind == RouteCommandButtonKind.Momentary ||
                card.Bindings.Any(binding => binding.Role == SignalBindingRole.PowerFeedback);
            if (card.Id == "equip.bucket" && card.StartButtonKind != RouteCommandButtonKind.Momentary)
                Add(errors, "card", card.Id, nameof(card.StartButtonKind), "ПУСК Кюбеля должен быть моментальной кнопкой.");
            if (card.StopButtonKind != RouteCommandButtonKind.Toggle)
                Add(errors, "card", card.Id, nameof(card.StopButtonKind), "СТОП должен быть обычным ToggleButton.");
            ValidateThickness(errors, "card", card.Id, nameof(card.Style.Margin), card.Style.Margin);
            ValidateThickness(errors, "card", card.Id, nameof(card.Style.Padding), card.Style.Padding);
            ValidateThickness(errors, "card", card.Id, nameof(card.Style.BorderThickness), card.Style.BorderThickness);
            ValidateCornerRadius(errors, "card", card.Id, nameof(card.Style.CornerRadius), card.Style.CornerRadius);
            ValidateColors(errors, "card", card.Id,
                (nameof(card.Style.BackgroundColor), card.Style.BackgroundColor),
                (nameof(card.Style.BorderColor), card.Style.BorderColor),
                (nameof(card.Style.TitleColor), card.Style.TitleColor),
                (nameof(card.Style.TextColor), card.Style.TextColor));
            var cardAllowedRoles = new List<SignalBindingRole>
            {
                SignalBindingRole.Text,
                SignalBindingRole.Value,
                SignalBindingRole.Visible,
                SignalBindingRole.StartCommand,
                SignalBindingRole.StopCommand,
                SignalBindingRole.PowerFeedback,
                SignalBindingRole.StartOffFeedback,
                SignalBindingRole.StopOffFeedback,
                SignalBindingRole.Fault,
                SignalBindingRole.Enabled,
            };
            if (!usesPowerFeedback)
            {
                cardAllowedRoles.Add(SignalBindingRole.UncheckedCommand);
                cardAllowedRoles.Add(SignalBindingRole.CheckedCommand);
            }
            if (card.StartButtonKind == RouteCommandButtonKind.Momentary &&
                card.Bindings.Any(binding => binding.Role == SignalBindingRole.StartCommand))
                cardAllowedRoles.Add(SignalBindingRole.CommandResetFeedback);
            ValidateBindings(errors, "card", card.Id, card.Bindings, cardAllowedRoles);
            ValidateCardStatusMapping(errors, card);
            ValidateCardParameters(errors, card);
            if (card.CanStart)
            {
                ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.StartCommand, SignalBindingDirection.ReadWrite);
            }
            if (card.CanStop)
            {
                ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.StopCommand, SignalBindingDirection.ReadWrite);
            }
            if (usesPowerFeedback)
            {
                ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.PowerFeedback, SignalBindingDirection.Read);
                var powerBinding = card.Bindings.FirstOrDefault(binding => binding.Role == SignalBindingRole.PowerFeedback);
                if (powerBinding is not null && card.Bindings.Any(binding =>
                        IsCommandRole(binding.Role) &&
                        string.Equals(binding.SignalId, powerBinding.SignalId, StringComparison.OrdinalIgnoreCase)))
                    Add(errors, "card", card.Id, nameof(card.Bindings), "PowerFeedback должен иметь отдельный SignalId от команд карточки.");
            }
            else
            {
                ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.UncheckedCommand, SignalBindingDirection.ReadWrite);
                ValidateRequiredBinding(errors, "card", card.Id, card.Bindings, SignalBindingRole.CheckedCommand, SignalBindingDirection.ReadWrite);
                var uncheckedBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.UncheckedCommand);
                var checkedBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.CheckedCommand);
                if (uncheckedBinding is not null && checkedBinding is not null &&
                    string.Equals(uncheckedBinding.SignalId, checkedBinding.SignalId, StringComparison.OrdinalIgnoreCase))
                {
                    Add(errors, "card", card.Id, nameof(card.Bindings), "UncheckedCommand и CheckedCommand должны использовать разные SignalId.");
                }
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

    private static void ValidateCardParameters(
        ICollection<RouteMapConfigurationError> errors,
        EquipmentCardConfiguration card)
    {
        var parameters = card.Parameters?.ToArray() ?? [];
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Title))
                Add(errors, "card", card.Id, nameof(parameter.Title), "Название настройки оборудования обязательно.");
            if (string.IsNullOrWhiteSpace(parameter.SignalId))
                Add(errors, "card", card.Id, nameof(parameter.SignalId), "SignalId настройки оборудования обязателен.");
            if (parameter.Role != SignalBindingRole.EquipmentParameter)
                Add(errors, "card", card.Id, nameof(parameter.Role), "Настройка оборудования должна использовать роль EquipmentParameter.");
            ValidateEnum(errors, "card", card.Id, nameof(parameter.Role), parameter.Role);
            ValidateEnum(errors, "card", card.Id, nameof(parameter.Direction), parameter.Direction);
            ValidateEnum(errors, "card", card.Id, nameof(parameter.ValueType), parameter.ValueType);
            ValidateEnum(errors, "card", card.Id, nameof(parameter.Visibility), parameter.Visibility);
        }

        foreach (var duplicate in parameters
                     .Where(parameter => !string.IsNullOrWhiteSpace(parameter.SignalId))
                     .GroupBy(parameter => parameter.SignalId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            Add(errors, "card", card.Id, nameof(EquipmentCardParameterConfiguration.SignalId),
                $"SignalId настройки оборудования '{duplicate.Key}' указан несколько раз.");
        }
    }

    internal static void ValidateCardStatusMapping(
        ICollection<RouteMapConfigurationError> errors,
        EquipmentCardConfiguration card)
    {
        if (card.StatusMapping is not { } mapping)
            return;

        const string scope = "card";
        const string property = nameof(EquipmentCardConfiguration.StatusMapping);
        if (!card.Bindings.Any(binding => IsStatusTextBinding(binding) && !string.IsNullOrWhiteSpace(binding.SignalId)))
            Add(errors, scope, card.Id, property, "Укажите SignalId в binding Text с направлением Read и типом UInt16 или Word.");

        ValidateStatusText(errors, card.Id, $"{property}.{nameof(mapping.UnknownText)}", mapping.UnknownText);
        ValidateStatusText(errors, card.Id, $"{property}.{nameof(mapping.UnavailableText)}", mapping.UnavailableText);

        var entriesProperty = $"{property}.{nameof(mapping.Entries)}";
        if (mapping.Entries is not { Count: >= 1 and <= 256 } entries)
        {
            Add(errors, scope, card.Id, entriesProperty, "Таблица статусов должна содержать от 1 до 256 строк.");
            return;
        }

        var codes = new HashSet<int>();
        for (var index = 0; index < entries.Count; index++)
        {
            var row = $"{entriesProperty}[{index}]";
            var entry = entries[index];
            if (entry is null)
            {
                Add(errors, scope, card.Id, row, "Строка таблицы статусов обязательна.");
                continue;
            }

            var codeProperty = $"{row}.{nameof(entry.Code)}";
            if (entry.Code is < ushort.MinValue or > ushort.MaxValue)
                Add(errors, scope, card.Id, codeProperty, "Укажите целый код статуса в диапазоне 0–65535.");
            else if (!codes.Add(entry.Code))
                Add(errors, scope, card.Id, codeProperty, $"Код статуса {entry.Code} уже указан.");

            ValidateStatusText(errors, card.Id, $"{row}.{nameof(entry.Text)}", entry.Text);
            if (entry.Color is not { Length: 7 or 9 } || !ColorPattern.IsMatch(entry.Color))
                Add(errors, scope, card.Id, $"{row}.{nameof(entry.Color)}", "Цвет должен иметь формат #RRGGBB или #AARRGGBB.");
            ValidateEnum(errors, scope, card.Id, $"{row}.{nameof(entry.Category)}", entry.Category);
            if (entry.Category == EquipmentStatusCategory.Fault && (!entry.BlockStart || !entry.BlockStop))
                Add(errors, scope, card.Id, row, "Аварийный статус должен блокировать ПУСК и СТОП.");
        }
    }

    internal static bool IsStatusTextBinding(SignalBindingConfiguration binding) =>
        binding.Role == SignalBindingRole.Text &&
        binding.Direction == SignalBindingDirection.Read &&
        binding.ValueType is SignalValueType.UInt16 or SignalValueType.Word;

    private static void ValidateStatusText(
        ICollection<RouteMapConfigurationError> errors,
        string cardId,
        string property,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100)
            Add(errors, "card", cardId, property, "Текст статуса должен содержать от 1 до 100 символов.");
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
            if (binding.Role is SignalBindingRole.Enabled or SignalBindingRole.PowerFeedback or SignalBindingRole.CommandResetFeedback)
            {
                if (binding.Direction != SignalBindingDirection.Read)
                    Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Direction), $"Binding {binding.Role} должен иметь направление Read.");
                if (binding.ValueType != Configurator.Application.Services.Signals.SignalValueType.Bool)
                    Add(errors, scope, objectId, nameof(SignalBindingConfiguration.ValueType), $"Binding {binding.Role} должен иметь тип Bool.");
            }
            if (isCommand && binding.Direction == SignalBindingDirection.Read)
                Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Direction), "Команда должна иметь направление Write или ReadWrite.");
            if (!isCommand && binding.Direction == SignalBindingDirection.Write)
                Add(errors, scope, objectId, nameof(SignalBindingConfiguration.Direction), "Состояние должно иметь направление Read или ReadWrite.");
        }
    }

    private static void ValidateTopBar(
        ICollection<RouteMapConfigurationError> errors,
        RouteMapConfigurationDocument document)
    {
        var topBar = document.TopBar;
        ValidatePresentLegacyTopBarBindings(errors, "automatic", topBar.Automatic, SignalBindingRole.AutomaticModeCommand);
        ValidatePresentLegacyTopBarBindings(errors, "manual", topBar.Manual, SignalBindingRole.ManualModeCommand);
        ValidateTopBarButton(
            errors,
            "reset",
            topBar.Reset,
            SignalBindingRole.ResetCommand,
            offFeedbackRole: null,
            requireOffFeedback: false);
        ValidateTopBarButton(
            errors,
            "emergency",
            topBar.Emergency,
            SignalBindingRole.EmergencyCommand,
            offFeedbackRole: null,
            requireOffFeedback: false);

        var commandSections = new (string Id, RouteTopBarButtonConfiguration Button, SignalBindingRole Role)[]
        {
            ("unload", topBar.Unload, SignalBindingRole.UnloadCommand),
            ("rotation", topBar.Rotation, SignalBindingRole.RotationCommand),
            ("load", topBar.Load, SignalBindingRole.LoadCommand),
        };
        foreach (var (id, button, role) in commandSections)
            ValidateTopBarButton(errors, id, button, role, offFeedbackRole: SignalBindingRole.CommandResetFeedback, requireOffFeedback: false);

        var commandBindings = commandSections.SelectMany(section => (section.Button?.Bindings ?? [])
            .Where(binding => binding is not null && binding.Role == section.Role)
            .Select(binding => (section.Id, Binding: binding))).ToArray();
        var writableSignalIds = (topBar.Reset?.Bindings ?? [])
            .Concat(topBar.Emergency?.Bindings ?? [])
            .Concat(document.Nodes.SelectMany(node => node.Bindings))
            .Concat(document.Segments.SelectMany(segment => segment.Bindings))
            .Concat(document.Segments.SelectMany(segment => segment.ActiveFragments).Select(fragment => fragment.Binding))
            .Concat(document.Cards.SelectMany(card => card.Bindings))
            .Where(binding => binding?.Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite)
            .Select(binding => binding.SignalId)
            .Concat(document.Cards.SelectMany(card => card.Parameters ?? [])
                .Where(parameter => parameter.Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite)
                .Select(parameter => parameter.SignalId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Reserve SignalIds owned by the runtime and by existing system commands.
        writableSignalIds.UnionWith([
            "system.reset", "system.emergency", "system.mode.automatic", "system.mode.manual",
            RouteMapSystemSignalIds.ConnectionConnected, RouteMapSystemSignalIds.ConnectionStatus,
            RouteMapSystemSignalIds.GlobalFault,
        ]);

        foreach (var group in commandBindings
                     .Where(command => !string.IsNullOrWhiteSpace(command.Binding.SignalId))
                     .GroupBy(command => command.Binding.SignalId, StringComparer.OrdinalIgnoreCase))
        {
            var sections = group.Select(command => command.Id).Distinct().ToArray();
            if (sections.Length == 1 && !writableSignalIds.Contains(group.Key))
                continue;

            foreach (var id in sections)
                Add(errors, "topBar", id, nameof(SignalBindingConfiguration.SignalId),
                    $"SignalId '{group.Key}' зарезервирован для другой команды или используется другой привязкой с записью.");
        }

        writableSignalIds.UnionWith(commandBindings.Select(command => command.Binding.SignalId));
        var resetFeedback = commandSections.SelectMany(section => (section.Button?.Bindings ?? [])
                .Select(binding => (Scope: "topBar", section.Id, Binding: binding)))
            .Concat(document.Cards.SelectMany(card => card.Bindings.Select(binding => (Scope: "card", card.Id, Binding: binding))))
            .Where(item => item.Binding?.Role == SignalBindingRole.CommandResetFeedback)
            .GroupBy(item => item.Binding.SignalId, StringComparer.OrdinalIgnoreCase);
        foreach (var group in resetFeedback)
        {
            if (group.Count() == 1 && !writableSignalIds.Contains(group.Key))
                continue;

            foreach (var item in group)
                Add(errors, item.Scope, item.Id, nameof(SignalBindingConfiguration.SignalId),
                    $"Ответ ПЛК '{group.Key}' должен иметь отдельный SignalId для каждой команды.");
        }
    }

    private static void ValidatePresentLegacyTopBarBindings(
        ICollection<RouteMapConfigurationError> errors,
        string id,
        RouteTopBarButtonConfiguration button,
        SignalBindingRole commandRole)
    {
        foreach (var role in new[] { commandRole, SignalBindingRole.Enabled })
        {
            var matches = button.Bindings.Where(binding => binding.Role == role).ToArray();
            if (matches.Length > 1)
                Add(errors, "topBar", id, nameof(button.Bindings), $"Роль {role} указана несколько раз.");
            foreach (var binding in matches)
            {
                var expectedDirection = role == SignalBindingRole.Enabled
                    ? SignalBindingDirection.Read
                    : SignalBindingDirection.ReadWrite;
                if (binding.Direction != expectedDirection)
                    Add(errors, "topBar", id, nameof(binding.Direction), $"Binding {role} должен иметь направление {expectedDirection}.");
                if (binding.ValueType != Configurator.Application.Services.Signals.SignalValueType.Bool)
                    Add(errors, "topBar", id, nameof(binding.ValueType), $"Binding {role} должен иметь тип Bool.");
            }
        }
    }

    private static void ValidateTopBarButton(
        ICollection<RouteMapConfigurationError> errors,
        string id,
        RouteTopBarButtonConfiguration? button,
        SignalBindingRole role,
        SignalBindingRole? offFeedbackRole,
        bool requireOffFeedback)
    {
        if (button is null)
        {
            Add(errors, "topBar", id, nameof(RouteTopBarButtonConfiguration.Bindings), "Секция кнопки обязательна.");
            return;
        }
        if (string.IsNullOrWhiteSpace(button.Text))
            Add(errors, "topBar", id, nameof(button.Text), "Текст кнопки обязателен.");
        if (button is RouteTopBarEmergencyButtonConfiguration emergency)
        {
            ValidateEnum(errors, "topBar", id, nameof(emergency.ButtonKind), emergency.ButtonKind);
            if (emergency.ButtonKind != RouteCommandButtonKind.Toggle)
                Add(errors, "topBar", id, nameof(emergency.ButtonKind), "АВАРИЯ должна быть обычным ToggleButton.");
            if (emergency.OffFeedbackEnabled)
                Add(errors, "topBar", id, nameof(emergency.OffFeedbackEnabled), "OffFeedback для АВАРИЯ больше не поддерживается.");
        }
        var allowedRoles = offFeedbackRole.HasValue
            ? [role, offFeedbackRole.Value, SignalBindingRole.Enabled]
            : new[] { role, SignalBindingRole.Enabled };
        var bindings = (button.Bindings ?? []).Where(binding => binding is not null).ToArray();
        if (button.Bindings is not null && bindings.Length != button.Bindings.Count)
            Add(errors, "topBar", id, nameof(button.Bindings), "Пустая сигнальная привязка недопустима.");
        ValidateBindings(errors, "topBar", id, bindings, allowedRoles);
        ValidateRequiredBinding(errors, "topBar", id, bindings, role, SignalBindingDirection.ReadWrite);
        if (requireOffFeedback && offFeedbackRole.HasValue)
            ValidateRequiredBinding(errors, "topBar", id, bindings, offFeedbackRole.Value, SignalBindingDirection.Read);
    }

    private static void ValidateTerminalAnchor(
        ICollection<RouteMapConfigurationError> errors,
        RouteMapConfigurationDocument document,
        RouteNodeConfiguration node)
    {
        if (node.IsLoader)
            Add(errors, "node", node.Id, nameof(node.IsLoader), "Терминальный якорь не может быть точкой возврата.");
        if (node.IsTarget)
            Add(errors, "node", node.Id, nameof(node.IsTarget), "Терминальный якорь не может быть точкой назначения.");
        if (node.MenuKind != RouteNodeMenuKind.None)
            Add(errors, "node", node.Id, nameof(node.MenuKind), "У терминального якоря не может быть меню.");
        if (node.State != RouteObjectState.Idle)
            Add(errors, "node", node.Id, nameof(node.State), "Терминальный якорь должен иметь нейтральное состояние Idle.");
        if (node.Bindings.Count > 0)
            Add(errors, "node", node.Id, nameof(node.Bindings), "У терминального якоря не может быть сигнальных привязок.");
        if (document.Segments.Count(segment => segment.FromNodeId == node.Id || segment.ToNodeId == node.Id) != 1)
            Add(errors, "node", node.Id, nameof(node.Id), "Терминальный якорь должен принадлежать ровно одной линии.");
    }

    private static void ValidateTerminalSegment(
        ICollection<RouteMapConfigurationError> errors,
        RouteSegmentConfiguration segment,
        RouteNodeConfiguration? from,
        RouteNodeConfiguration? to)
    {
        var fromIsAnchor = from?.Kind == RouteNodeKind.TerminalAnchor;
        var toIsAnchor = to?.Kind == RouteNodeKind.TerminalAnchor;
        if (segment.Kind != RouteSegmentKind.DeadEnd)
        {
            if (fromIsAnchor || toIsAnchor)
                Add(errors, "segment", segment.Id, nameof(segment.Kind), "Терминальный якорь допускается только для линии DeadEnd.");
            return;
        }

        ValidateNullablePositive(errors, "segment", segment.Id, nameof(segment.TerminalCapLength), segment.TerminalCapLength);
        if (segment.TerminalSide is not (RouteTerminalSide.From or RouteTerminalSide.To) ||
            fromIsAnchor != (segment.TerminalSide == RouteTerminalSide.From) ||
            toIsAnchor != (segment.TerminalSide == RouteTerminalSide.To))
        {
            Add(errors, "segment", segment.Id, nameof(segment.TerminalSide), "Укажите сторону единственного терминального якоря линии DeadEnd.");
        }

        if (from is not null && to is not null && fromIsAnchor != toIsAnchor &&
            !RouteSegmentGeometry.TryCreateTerminalCap(segment.TerminalSide, segment.TerminalCapLength, segment.Style.Thickness,
                new Avalonia.Point(from.X, from.Y), new Avalonia.Point(to.X, to.Y), out _))
        {
            Add(errors, "segment", segment.Id, nameof(segment.ToNodeId),
                "Координаты концов и длина поперечины должны образовывать корректную геометрию тупика.");
        }
    }

    private static void ValidateSegmentActiveFragments(
        ICollection<RouteMapConfigurationError> errors,
        RouteMapConfigurationDocument document,
        RouteSegmentConfiguration segment)
    {
        var fragments = segment.ActiveFragments?.ToArray() ?? [];
        if (RouteSegmentActiveFragmentSynchronizer.CalculateFragmentCount(document, segment) is not { } expectedCount)
        {
            Add(errors, "segment", segment.Id, nameof(segment.ActiveFragments), "Длина линии и настройки деления задают слишком много отрезков.");
            return;
        }
        if (expectedCount <= 1)
        {
            if (fragments.Length > 0)
                Add(errors, "segment", segment.Id, nameof(segment.ActiveFragments), "ActiveRouteFragment должен быть пустым для линии без деления на отрезки.");
            return;
        }

        if (fragments.Length != expectedCount)
        {
            Add(errors, "segment", segment.Id, nameof(segment.ActiveFragments), $"Линия должна иметь {expectedCount} binding ActiveRouteFragment.");
            if (segment.Kind == RouteSegmentKind.DeadEnd)
                return;
        }

        foreach (var duplicate in fragments.GroupBy(fragment => fragment.Index).Where(group => group.Count() > 1))
            Add(errors, "segment", segment.Id, nameof(RouteSegmentActiveFragmentConfiguration.Index), $"Индекс отрезка {duplicate.Key} указан несколько раз.");

        for (var index = 1; index <= expectedCount; index++)
        {
            if (fragments.All(fragment => fragment.Index != index))
                Add(errors, "segment", segment.Id, nameof(RouteSegmentActiveFragmentConfiguration.Index), $"Нет binding ActiveRouteFragment для отрезка {index}.");
        }

        foreach (var fragment in fragments)
        {
            if (fragment.Index < 1 || fragment.Index > expectedCount)
                Add(errors, "segment", segment.Id, nameof(fragment.Index), $"Индекс отрезка должен быть от 1 до {expectedCount}.");

            var binding = fragment.Binding;
            if (binding.Role != SignalBindingRole.ActiveRouteFragment)
                Add(errors, "segment", segment.Id, nameof(binding.Role), "Отрезок линии должен использовать роль ActiveRouteFragment.");
            if (string.IsNullOrWhiteSpace(binding.SignalId))
                Add(errors, "segment", segment.Id, nameof(binding.SignalId), "SignalId отрезка обязателен.");
            if (binding.Direction != SignalBindingDirection.Read)
                Add(errors, "segment", segment.Id, nameof(binding.Direction), "ActiveRouteFragment должен иметь направление Read.");
            if (binding.ValueType != Configurator.Application.Services.Signals.SignalValueType.Bool)
                Add(errors, "segment", segment.Id, nameof(binding.ValueType), "ActiveRouteFragment должен иметь тип Bool.");
        }
    }

    private static bool IsCommandRole(SignalBindingRole role) => role is
        SignalBindingRole.StartCommand or
        SignalBindingRole.StopCommand or
        SignalBindingRole.UncheckedCommand or
        SignalBindingRole.CheckedCommand or
        SignalBindingRole.TargetCommand or
        SignalBindingRole.LoaderCommand or
        SignalBindingRole.AutomaticModeCommand or
        SignalBindingRole.ManualModeCommand or
        SignalBindingRole.ResetCommand or
        SignalBindingRole.EmergencyCommand or
        SignalBindingRole.UnloadCommand or
        SignalBindingRole.RotationCommand or
        SignalBindingRole.LoadCommand;

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
