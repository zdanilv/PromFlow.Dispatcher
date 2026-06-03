using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;

namespace Configurator.Application.Services.Modbus.Validation;

/// <summary>
/// Валидирует именованные карты данных Modbus перед использованием в высокоуровневом сервисе.
/// </summary>
public sealed class ModbusDataMapValidator : IModbusDataMapValidator
{
    private const int MaxCoils = 2000;
    private const int MaxRegisters = 123;

    /// <summary>
    /// Проверяет все настроенные точки и их диапазоны для выбранной роли.
    /// </summary>
    public ModbusOperationResult Validate(ModbusOptions options, ModbusRunMode mode)
    {
        ArgumentNullException.ThrowIfNull(options);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var point in options.DataMap)
        {
            var pointValidation = ValidatePoint(point, names);
            if (!pointValidation.Succeeded)
            {
                return pointValidation;
            }

            var rangeValidation = ValidateRoleRange(point, options, mode);
            if (!rangeValidation.Succeeded)
            {
                return rangeValidation;
            }
        }

        return ModbusOperationResult.Success();
    }

    /// <summary>
    /// Проверяет одну настроенную точку и отслеживает дубли имен.
    /// </summary>
    private static ModbusOperationResult ValidatePoint(
        ModbusDataPointOptions point,
        HashSet<string> names)
    {
        if (string.IsNullOrWhiteSpace(point.Name))
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointNameEmpty",
                "Modbus data point name must not be empty.");
        }

        if (!names.Add(point.Name.Trim()))
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointDuplicate",
                $"Modbus data point '{point.Name}' is duplicated.");
        }

        if (point.Address < 0)
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointAddressInvalid",
                $"Modbus data point '{point.Name}' has a negative address.");
        }

        if (point.Length <= 0)
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointLengthInvalid",
                $"Modbus data point '{point.Name}' length must be greater than zero.");
        }

        if (!Enum.IsDefined(point.Area))
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointAreaUnsupported",
                $"Modbus data point '{point.Name}' uses unsupported area '{point.Area}'.");
        }

        if (!Enum.IsDefined(point.Access))
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointAccessUnsupported",
                $"Modbus data point '{point.Name}' uses unsupported access '{point.Access}'.");
        }

        if (!Enum.IsDefined(point.Type))
        {
            return ModbusOperationResult.Failure(
                "ModbusDataPointTypeUnsupported",
                $"Modbus data point '{point.Name}' uses unsupported type '{point.Type}'.");
        }

        return point.Area switch
        {
            ModbusDataArea.Coil => ValidateCoil(point),
            ModbusDataArea.HoldingRegister => ValidateRegister(point),
            _ => ModbusOperationResult.Failure(
                "ModbusDataPointAreaUnsupported",
                $"Modbus data point '{point.Name}' uses unsupported area '{point.Area}'.")
        };
    }

    /// <summary>
    /// Проверяет ограничения типа и длины для Coil.
    /// </summary>
    private static ModbusOperationResult ValidateCoil(ModbusDataPointOptions point)
    {
        if (point.Type != ModbusValueType.Bool)
        {
            return ModbusOperationResult.Failure(
                "ModbusCoilTypeInvalid",
                $"Coil data point '{point.Name}' must use Bool type.");
        }

        if (point.Length != 1)
        {
            return ModbusOperationResult.Failure(
                "ModbusCoilLengthInvalid",
                $"Coil data point '{point.Name}' length must be 1.");
        }

        if (point.Address >= MaxCoils)
        {
            return ModbusOperationResult.Failure(
                "ModbusCoilAddressInvalid",
                $"Coil data point '{point.Name}' address is outside 0..{MaxCoils - 1}.");
        }

        return ModbusOperationResult.Success();
    }

    /// <summary>
    /// Проверяет ограничения типа и длины для Holding Register.
    /// </summary>
    private static ModbusOperationResult ValidateRegister(ModbusDataPointOptions point)
    {
        if (point.Type == ModbusValueType.Bool)
        {
            return ModbusOperationResult.Failure(
                "ModbusRegisterTypeInvalid",
                $"Holding register data point '{point.Name}' cannot use Bool type.");
        }

        var requiredLength = RequiredRegisterLength(point);
        if (point.Length != requiredLength && point.Type != ModbusValueType.String)
        {
            return ModbusOperationResult.Failure(
                "ModbusRegisterLengthInvalid",
                $"Holding register data point '{point.Name}' length must be {requiredLength} for {point.Type}.");
        }

        if (point.Type == ModbusValueType.String && point.Length < 1)
        {
            return ModbusOperationResult.Failure(
                "ModbusRegisterLengthInvalid",
                $"String data point '{point.Name}' length must be at least 1.");
        }

        if (point.Address + point.Length > MaxRegisters)
        {
            return ModbusOperationResult.Failure(
                "ModbusRegisterAddressInvalid",
                $"Holding register data point '{point.Name}' range is outside 0..{MaxRegisters - 1}.");
        }

        return ModbusOperationResult.Success();
    }

    /// <summary>
    /// Проверяет, что точка помещается в диапазон endpoint выбранной роли.
    /// </summary>
    private static ModbusOperationResult ValidateRoleRange(
        ModbusDataPointOptions point,
        ModbusOptions options,
        ModbusRunMode mode)
    {
        return mode switch
        {
            ModbusRunMode.Client => ValidateEndpointRange(point, options.Client, "client"),
            ModbusRunMode.Server => ValidateEndpointRange(point, options.Server, "server"),
            _ => ModbusOperationResult.Success()
        };
    }

    /// <summary>
    /// Проверяет точку относительно настроенных размеров и включенных областей endpoint.
    /// </summary>
    private static ModbusOperationResult ValidateEndpointRange(
        ModbusDataPointOptions point,
        ModbusEndpointOptions endpoint,
        string role)
    {
        if (point.Area == ModbusDataArea.Coil)
        {
            if (!endpoint.CoilsEnabled && role == "client")
            {
                return ModbusOperationResult.Failure(
                    "ModbusCoilsDisabled",
                    $"Coils are disabled for the Modbus {role}, but '{point.Name}' uses Coil.");
            }

            if (point.Address + point.Length > endpoint.CoilCount)
            {
                return ModbusOperationResult.Failure(
                    "ModbusCoilRangeInvalid",
                    $"Coil data point '{point.Name}' is outside the configured {role} coil range.");
            }
        }
        else
        {
            if (!endpoint.HoldingRegistersEnabled && role == "client")
            {
                return ModbusOperationResult.Failure(
                    "ModbusRegistersDisabled",
                    $"Holding registers are disabled for the Modbus {role}, but '{point.Name}' uses HoldingRegister.");
            }

            if (point.Address + point.Length > endpoint.RegisterCount)
            {
                return ModbusOperationResult.Failure(
                    "ModbusRegisterRangeInvalid",
                    $"Holding register data point '{point.Name}' is outside the configured {role} register range.");
            }
        }

        return ModbusOperationResult.Success();
    }

    /// <summary>
    /// Возвращает ширину в регистрах, требуемую логическим типом значения.
    /// </summary>
    private static int RequiredRegisterLength(ModbusDataPointOptions point)
        => point.Type switch
        {
            ModbusValueType.UInt16 => 1,
            ModbusValueType.Int => 1,
            ModbusValueType.Real => 2,
            ModbusValueType.Date => 2,
            ModbusValueType.Dword => 2,
            ModbusValueType.String => point.Length,
            _ => 1
        };
}
