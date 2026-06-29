namespace Configurator.Application.Services.Modbus.Configuration;

/// <summary>
/// Тип пользовательского уведомления по Modbus-биту тревоги.
/// </summary>
public enum ModbusAlarmKind
{
    /// <summary>
    /// Аварийное сообщение.
    /// </summary>
    Fault,

    /// <summary>
    /// Предупреждение с повторным подтверждением.
    /// </summary>
    Confirmation,

    /// <summary>
    /// Обычное информационное сообщение.
    /// </summary>
    Message
}
