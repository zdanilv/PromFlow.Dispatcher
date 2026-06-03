using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.Modbus;

/// <summary>
/// Строка типизированной записи в Holding Registers.
/// </summary>
public sealed class ModbusTypedRegisterRow : ReactiveObject
{
    private readonly Func<int, ushort[], Task> _writeAsync;
    private readonly Action<string>? _reportError;
    private string _valueText = string.Empty;
    private string _errorText = string.Empty;
    private bool _suppressWrite;

    /// <summary>
    /// Создает строку типизированного значения.
    /// </summary>
    public ModbusTypedRegisterRow(
        string label,
        int address,
        int registerCount,
        ModbusRegisterValueKind kind,
        Func<int, ushort[], Task> writeAsync,
        Action<string>? reportError = null)
    {
        Label = label;
        Address = address;
        RegisterCount = registerCount;
        Kind = kind;
        _writeAsync = writeAsync;
        _reportError = reportError;
    }

    /// <summary>
    /// Название значения в UI.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Начальный нулевой адрес регистра.
    /// </summary>
    public int Address { get; }

    /// <summary>
    /// Количество регистров для значения.
    /// </summary>
    public int RegisterCount { get; }

    /// <summary>
    /// Тип значения.
    /// </summary>
    public ModbusRegisterValueKind Kind { get; }

    /// <summary>
    /// Текстовое значение для редактирования.
    /// </summary>
    public string ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _valueText, value);

            if (_suppressWrite)
            {
                return;
            }

            if (!TryEncode(value, out var registers, out var error))
            {
                ErrorText = error;
                _reportError?.Invoke($"{Label}: {error}");
                return;
            }

            ErrorText = string.Empty;
            _ = _writeAsync(Address, registers);
        }
    }

    /// <summary>
    /// Ошибка ввода или декодирования.
    /// </summary>
    public string ErrorText
    {
        get => _errorText;
        private set => this.RaiseAndSetIfChanged(ref _errorText, value);
    }

    /// <summary>
    /// Обновляет текст из raw-регистров без обратной записи.
    /// </summary>
    public void SetFromSnapshot(IReadOnlyList<ushort> registers)
    {
        _suppressWrite = true;

        try
        {
            ValueText = Kind switch
            {
                ModbusRegisterValueKind.Int => ModbusRegistersCodec.DecodeInt(registers, Address).ToString(CultureInfo.InvariantCulture),
                ModbusRegisterValueKind.Real => ModbusRegistersCodec.DecodeReal(registers, Address).ToString("F2", CultureInfo.InvariantCulture),
                ModbusRegisterValueKind.String => ModbusRegistersCodec.DecodeString(registers, Address, RegisterCount),
                ModbusRegisterValueKind.Date => ModbusRegistersCodec.DecodeDate(registers, Address).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ModbusRegisterValueKind.Dword => ModbusRegistersCodec.DecodeDword(registers, Address).ToString(CultureInfo.InvariantCulture),
                _ => string.Empty
            };
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            _suppressWrite = false;
        }
    }

    private bool TryEncode(string text, out ushort[] registers, out string error)
    {
        try
        {
            registers = Kind switch
            {
                ModbusRegisterValueKind.Int => ModbusRegistersCodec.EncodeInt(int.Parse(text, CultureInfo.InvariantCulture)),
                ModbusRegisterValueKind.Real => ModbusRegistersCodec.EncodeReal(float.Parse(text, CultureInfo.InvariantCulture)),
                ModbusRegisterValueKind.String => ModbusRegistersCodec.EncodeString(text, RegisterCount),
                ModbusRegisterValueKind.Date => ModbusRegistersCodec.EncodeDate(DateTime.Parse(text, CultureInfo.InvariantCulture)),
                ModbusRegisterValueKind.Dword => ModbusRegistersCodec.EncodeDword(uint.Parse(text, CultureInfo.InvariantCulture)),
                _ => Array.Empty<ushort>()
            };
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            registers = Array.Empty<ushort>();
            error = ex.Message;
            return false;
        }
    }
}
