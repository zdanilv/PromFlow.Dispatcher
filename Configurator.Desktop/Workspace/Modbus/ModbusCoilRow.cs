using ReactiveUI;
using System;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.Modbus;

/// <summary>
/// Строка Coil в UI: хранит значение и отправляет запись при изменении пользователем.
/// </summary>
public sealed class ModbusCoilRow : ReactiveObject
{
    private readonly Func<int, bool, Task> _writeAsync;
    private bool _value;
    private bool _suppressWrite;

    /// <summary>
    /// Создает строку Coil для указанного адреса.
    /// </summary>
    public ModbusCoilRow(int address, Func<int, bool, Task> writeAsync)
    {
        Address = address;
        _writeAsync = writeAsync;
    }

    /// <summary>
    /// Нулевой адрес Coil.
    /// </summary>
    public int Address { get; }

    /// <summary>
    /// Текущее значение Coil.
    /// </summary>
    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _value, value);
            this.RaisePropertyChanged(nameof(ValueText));

            if (!_suppressWrite)
            {
                _ = _writeAsync(Address, value);
            }
        }
    }

    /// <summary>
    /// Короткое текстовое значение для таблицы.
    /// </summary>
    public string ValueText => Value ? "ON" : "OFF";

    /// <summary>
    /// Применяет значение из snapshot без обратной записи в Modbus.
    /// </summary>
    public void SetFromSnapshot(bool value)
    {
        _suppressWrite = true;
        Value = value;
        _suppressWrite = false;
    }
}
