using ReactiveUI;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.Modbus;

/// <summary>
/// Строка Holding Register в UI: редактирует raw ushort и пишет его в Modbus.
/// </summary>
public sealed class ModbusRegisterRow : ReactiveObject
{
    private readonly Func<int, ushort, Task> _writeAsync;
    private ushort _rawValue;
    private string _rawValueText = "0";
    private string _errorText = string.Empty;
    private bool _suppressWrite;

    /// <summary>
    /// Создает строку регистра для указанного адреса.
    /// </summary>
    public ModbusRegisterRow(int address, Func<int, ushort, Task> writeAsync)
    {
        Address = address;
        _writeAsync = writeAsync;
    }

    /// <summary>
    /// Нулевой адрес регистра.
    /// </summary>
    public int Address { get; }

    /// <summary>
    /// Raw ushort значение регистра.
    /// </summary>
    public ushort RawValue
    {
        get => _rawValue;
        private set => this.RaiseAndSetIfChanged(ref _rawValue, value);
    }

    /// <summary>
    /// Текст поля ввода raw ushort.
    /// </summary>
    public string RawValueText
    {
        get => _rawValueText;
        set
        {
            if (_rawValueText == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _rawValueText, value);

            if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                ErrorText = "0..65535";
                return;
            }

            ErrorText = string.Empty;
            RawValue = parsed;

            if (!_suppressWrite)
            {
                _ = _writeAsync(Address, parsed);
            }
        }
    }

    /// <summary>
    /// Ошибка ввода значения.
    /// </summary>
    public string ErrorText
    {
        get => _errorText;
        private set => this.RaiseAndSetIfChanged(ref _errorText, value);
    }

    /// <summary>
    /// Применяет значение из snapshot без обратной записи в Modbus.
    /// </summary>
    public void SetFromSnapshot(ushort value)
    {
        _suppressWrite = true;
        RawValue = value;
        RawValueText = value.ToString(CultureInfo.InvariantCulture);
        _suppressWrite = false;
    }
}
