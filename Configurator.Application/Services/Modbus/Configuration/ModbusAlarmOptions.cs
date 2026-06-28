namespace Configurator.Application.Services.Modbus.Configuration;

/// <summary>
/// Настройка одного пользовательского уведомления, связанного с Modbus-битом.
/// </summary>
public sealed class ModbusAlarmOptions
{
    /// <summary>
    /// Уникальный идентификатор записи AlarmMap.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Включена ли тревога.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Визуальный тип диалога.
    /// </summary>
    public ModbusAlarmKind Kind { get; set; } = ModbusAlarmKind.Fault;

    /// <summary>
    /// Текст, который показывается оператору.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Бит, который активирует диалог.
    /// </summary>
    public ModbusBitAddressOptions Alarm { get; set; } = new();

    /// <summary>
    /// Бит, в который записывается импульс после нажатия "Хорошо".
    /// </summary>
    public ModbusBitAddressOptions Acknowledgement { get; set; } = new();

    /// <summary>
    /// Интервал повторного показа, пока Alarm остается активным.
    /// </summary>
    public int RepeatIntervalMs { get; set; } = 60000;

    /// <summary>
    /// Длительность acknowledgement-импульса.
    /// </summary>
    public int AcknowledgementPulseDurationMs { get; set; } = 300;

    /// <summary>
    /// Создает независимую копию настройки тревоги.
    /// </summary>
    public ModbusAlarmOptions Clone()
        => new()
        {
            Id = Id,
            Enabled = Enabled,
            Kind = Kind,
            Message = Message,
            Alarm = Alarm.Clone(),
            Acknowledgement = Acknowledgement.Clone(),
            RepeatIntervalMs = RepeatIntervalMs,
            AcknowledgementPulseDurationMs = AcknowledgementPulseDurationMs
        };
}
