using System.Globalization;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public static class EquipmentStatusResolver
{
    public static EquipmentStatusPresentation Resolve(
        EquipmentStatusMapping mapping,
        SignalBinding? textBinding,
        SignalValue? signal,
        bool isConnectionAvailable,
        bool hasGlobalFault,
        bool hasLocalFault,
        bool isEnabled,
        RouteMapPaletteSettings palette)
    {
        if (!isConnectionAvailable)
            return Present(null, "Не в сети", palette.Offline, null, EquipmentStatusQuality.Disconnected,
                "Связь недоступна", RouteObjectState.Offline);

        var (quality, code, reason) = ReadCode(textBinding, signal);
        var entry = code is { } value ? mapping.Entries.FirstOrDefault(candidate => candidate.Code == value) : null;

        if (hasGlobalFault || hasLocalFault)
        {
            var faultText = hasGlobalFault ? "Авария системы" : "Авария оборудования";
            return Present(entry?.Code, faultText, palette.Fault, EquipmentStatusCategory.Fault,
                EquipmentStatusQuality.ForcedFault, faultText, RouteObjectState.Fault);
        }

        if (entry?.Category == EquipmentStatusCategory.Fault)
            return Present(code, entry.Text, entry.Color, entry.Category, EquipmentStatusQuality.Known,
                "Аварийный статус", RouteObjectState.Fault, isTrusted: true);

        if (entry is null && quality == EquipmentStatusQuality.Known)
        {
            quality = EquipmentStatusQuality.UnknownCode;
            reason = "Код отсутствует в таблице статусов";
        }

        var text = entry?.Text ?? (quality == EquipmentStatusQuality.UnknownCode
            ? $"{mapping.UnknownText} (код {FormatCode(code)})"
            : mapping.UnavailableText);
        var category = entry?.Category ?? (quality == EquipmentStatusQuality.UnknownCode
            ? EquipmentStatusCategory.Warning
            : (EquipmentStatusCategory?)null);

        if (!isEnabled)
            return Present(code, text, palette.Disabled, category, quality,
                "Оборудование отключено (Enabled=false). " + reason, RouteObjectState.Disabled,
                isTrusted: entry is not null);

        if (entry is null)
            return Present(code, text, category == EquipmentStatusCategory.Warning ? palette.Warning : palette.MutedText,
                category, quality, reason, category == EquipmentStatusCategory.Warning
                    ? RouteObjectState.Warning
                    : RouteObjectState.Unknown);

        return Present(code, text, entry.Color, category, quality, reason,
            category == EquipmentStatusCategory.Warning ? RouteObjectState.Warning : null,
            isTrusted: true, blockStart: entry.BlockStart, blockStop: entry.BlockStop);

        EquipmentStatusPresentation Present(
            ushort? statusCode,
            string statusText,
            string color,
            EquipmentStatusCategory? statusCategory,
            EquipmentStatusQuality statusQuality,
            string diagnosticReason,
            RouteObjectState? stateOverride,
            bool isTrusted = false,
            bool blockStart = true,
            bool blockStop = true)
        {
            var commands = (blockStart, blockStop) switch
            {
                (true, true) => "Статус запрещает ПУСК и СТОП.",
                (true, false) => "Статус запрещает ПУСК.",
                (false, true) => "Статус запрещает СТОП.",
                _ => "Дополнительных запретов ПУСК и СТОП по статусу нет."
            };
            var signalId = textBinding?.SignalId ?? "не настроен";
            var trust = isTrusted ? "достоверен" : "не подтверждён для управления";
            var diagnostic = $"SignalId: {signalId}\nКод: {FormatCode(statusCode)}\n" +
                $"Качество: {statusQuality}. Статус {trust}. {diagnosticReason}.\n{commands}";
            return new(statusCode, statusText, color, statusCategory, statusQuality,
                blockStart, blockStop, isTrusted, diagnostic, stateOverride);
        }
    }

    private static (EquipmentStatusQuality Quality, ushort? Code, string Reason) ReadCode(
        SignalBinding? binding, SignalValue? signal)
    {
        if (binding is null || signal is null ||
            !string.Equals(signal.SignalId, binding.SignalId, StringComparison.OrdinalIgnoreCase))
            return (EquipmentStatusQuality.Missing, null, "Значение Text отсутствует");

        if (!signal.IsQualityGood || signal.IsStale)
            return (EquipmentStatusQuality.BadOrStale, null,
                signal.IsStale ? "Значение Text устарело" : "Недостоверное значение Text");

        ushort? code = signal.Value switch
        {
            byte value => value,
            ushort value => value,
            uint value when value <= ushort.MaxValue => (ushort)value,
            ulong value when value <= ushort.MaxValue => (ushort)value,
            _ => null
        };
        if (binding.Role != SignalBindingRole.Text || binding.Direction != SignalBindingDirection.Read ||
            binding.ValueType is not (SignalValueType.UInt16 or SignalValueType.Word) ||
            signal.ValueType is not (SignalValueType.UInt16 or SignalValueType.Word) || code is null)
        {
            return (EquipmentStatusQuality.WrongType, null,
                $"Ожидается Text Read/UInt16 или Word, получено {signal.ValueType} ({signal.Value?.GetType().Name ?? "null"})");
        }

        return (EquipmentStatusQuality.Known, code, "Значение Text прочитано");
    }

    private static string FormatCode(ushort? code) => code?.ToString(CultureInfo.InvariantCulture) ?? "—";
}
