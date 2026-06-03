namespace Configurator.Desktop.Workspace.Modbus;

/// <summary>
/// Тип значения для удобной записи группы Holding Registers.
/// </summary>
public enum ModbusRegisterValueKind
{
    /// <summary>
    /// 16-битное целое значение.
    /// </summary>
    Int,

    /// <summary>
    /// 32-битное число с плавающей точкой.
    /// </summary>
    Real,

    /// <summary>
    /// ASCII-строка.
    /// </summary>
    String,

    /// <summary>
    /// Дата и время как Unix timestamp.
    /// </summary>
    Date,

    /// <summary>
    /// 32-битное целое без знака.
    /// </summary>
    Dword
}
