using Configurator.Application.Services.Modbus.Configuration;

namespace Configurator.Application.Services.Dialogs;

/// <summary>
/// Текстовое содержимое операторского уведомления о тревоге.
/// </summary>
public sealed record AlarmNotificationContent(
    ModbusAlarmKind Kind,
    string Message,
    string? RegisterValueText = null)
{
    public bool HasRegisterValueText => !string.IsNullOrWhiteSpace(RegisterValueText);
}
