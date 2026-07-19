using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Validation;

/// <summary>
/// Валидирует карту пользовательских тревог, живущую в секции Modbus.
/// </summary>
public sealed class ModbusAlarmMapValidator : IModbusAlarmMapValidator
{
    private const int MaxCoils = 2000;
    private const int MaxRegisters = 123;
    private const int MinRepeatIntervalMs = 1000;
    private const int MaxRepeatIntervalMs = 86400000;
    private const int MinPulseDurationMs = 1;
    private const int MaxPulseDurationMs = 60000;

    public ModbusOperationResult Validate(ModbusOptions options, ModbusRunMode mode)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alarm in options.AlarmMap)
        {
            var alarmValidation = ValidateAlarm(alarm, ids);
            if (!alarmValidation.Succeeded)
            {
                return alarmValidation;
            }

            var rangeValidation = ValidateRoleRange(alarm, options, mode);
            if (!rangeValidation.Succeeded)
            {
                return rangeValidation;
            }
        }

        return ModbusOperationResult.Success();
    }

    private static ModbusOperationResult ValidateAlarm(
        ModbusAlarmOptions alarm,
        HashSet<string> ids)
    {
        if (string.IsNullOrWhiteSpace(alarm.Id))
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmIdEmpty",
                "Modbus alarm id must not be empty.");
        }

        if (!ids.Add(alarm.Id.Trim()))
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmDuplicate",
                $"Modbus alarm '{alarm.Id}' is duplicated.");
        }

        if (!Enum.IsDefined(alarm.Kind))
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmKindUnsupported",
                $"Modbus alarm '{alarm.Id}' uses unsupported kind '{alarm.Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(alarm.Message))
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmMessageEmpty",
                $"Modbus alarm '{alarm.Id}' message must not be empty.");
        }

        if (alarm.RegisterValueEnabled
            && alarm.RegisterValueAddress is < 0 or >= MaxRegisters)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRegisterValueAddressInvalid",
                $"Modbus alarm '{alarm.Id}' register value address must be in range 0..{MaxRegisters - 1}.");
        }

        var alarmAddressValidation = ValidateAddress(alarm.Id, alarm.Alarm, "alarm");
        if (!alarmAddressValidation.Succeeded)
        {
            return alarmAddressValidation;
        }

        var acknowledgementAddressValidation = ValidateAddress(alarm.Id, alarm.Acknowledgement, "acknowledgement");
        if (!acknowledgementAddressValidation.Succeeded)
        {
            return acknowledgementAddressValidation;
        }

        if (SameAddress(alarm.Alarm, alarm.Acknowledgement))
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmAcknowledgementAddressConflict",
                $"Modbus alarm '{alarm.Id}' uses the same bit for alarm and acknowledgement.");
        }

        if (alarm.RepeatIntervalMs is < MinRepeatIntervalMs or > MaxRepeatIntervalMs)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRepeatIntervalInvalid",
                $"Modbus alarm '{alarm.Id}' repeat interval must be in range {MinRepeatIntervalMs}..{MaxRepeatIntervalMs} ms.");
        }

        if (alarm.AcknowledgementPulseDurationMs is < MinPulseDurationMs or > MaxPulseDurationMs)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmPulseDurationInvalid",
                $"Modbus alarm '{alarm.Id}' acknowledgement pulse duration must be in range {MinPulseDurationMs}..{MaxPulseDurationMs} ms.");
        }

        return ModbusOperationResult.Success();
    }

    private static ModbusOperationResult ValidateAddress(
        string alarmId,
        ModbusBitAddressOptions address,
        string role)
    {
        if (!Enum.IsDefined(address.Area))
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmAddressAreaUnsupported",
                $"Modbus alarm '{alarmId}' {role} address uses unsupported area '{address.Area}'.");
        }

        if (address.Address < 0)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmAddressInvalid",
                $"Modbus alarm '{alarmId}' {role} address must not be negative.");
        }

        return address.Area switch
        {
            ModbusDataArea.Coil => ValidateCoilAddress(alarmId, address, role),
            ModbusDataArea.HoldingRegister => ValidateRegisterBitAddress(alarmId, address, role),
            _ => ModbusOperationResult.Failure(
                "ModbusAlarmAddressAreaUnsupported",
                $"Modbus alarm '{alarmId}' {role} address uses unsupported area '{address.Area}'.")
        };
    }

    private static ModbusOperationResult ValidateCoilAddress(
        string alarmId,
        ModbusBitAddressOptions address,
        string role)
    {
        if (address.BitIndex is not null)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmCoilBitIndexInvalid",
                $"Modbus alarm '{alarmId}' {role} Coil address must not define BitIndex.");
        }

        if (address.Address >= MaxCoils)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmCoilAddressInvalid",
                $"Modbus alarm '{alarmId}' {role} Coil address is outside 0..{MaxCoils - 1}.");
        }

        return ModbusOperationResult.Success();
    }

    private static ModbusOperationResult ValidateRegisterBitAddress(
        string alarmId,
        ModbusBitAddressOptions address,
        string role)
    {
        if (address.BitIndex is < 0 or > 15 or null)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRegisterBitInvalid",
                $"Modbus alarm '{alarmId}' {role} Holding Register address must define BitIndex in range 0..15.");
        }

        if (address.Address >= MaxRegisters)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRegisterAddressInvalid",
                $"Modbus alarm '{alarmId}' {role} Holding Register address is outside 0..{MaxRegisters - 1}.");
        }

        return ModbusOperationResult.Success();
    }

    private static ModbusOperationResult ValidateRoleRange(
        ModbusAlarmOptions alarm,
        ModbusOptions options,
        ModbusRunMode mode)
    {
        return mode switch
        {
            ModbusRunMode.Client => ValidateEndpointRange(alarm, options.Client, "client"),
            ModbusRunMode.Server => ValidateEndpointRange(alarm, options.Server, "server"),
            ModbusRunMode.Both => ValidateEndpointRange(alarm, options.Client, "client") is { Succeeded: false } client
                ? client
                : ValidateEndpointRange(alarm, options.Server, "server"),
            _ => ModbusOperationResult.Success()
        };
    }

    private static ModbusOperationResult ValidateEndpointRange(
        ModbusAlarmOptions alarm,
        ModbusEndpointOptions endpoint,
        string role)
    {
        var alarmRange = ValidateEndpointAddress(alarm.Id, alarm.Alarm, endpoint, role, "alarm");
        if (!alarmRange.Succeeded)
        {
            return alarmRange;
        }

        var acknowledgementRange = ValidateEndpointAddress(alarm.Id, alarm.Acknowledgement, endpoint, role, "acknowledgement");
        if (!acknowledgementRange.Succeeded)
        {
            return acknowledgementRange;
        }

        return ValidateRegisterValueRange(alarm, endpoint, role);
    }

    private static ModbusOperationResult ValidateEndpointAddress(
        string alarmId,
        ModbusBitAddressOptions address,
        ModbusEndpointOptions endpoint,
        string runtimeRole,
        string addressRole)
    {
        if (address.Area == ModbusDataArea.Coil)
        {
            if (!endpoint.CoilsEnabled)
            {
                return ModbusOperationResult.Failure(
                    "ModbusAlarmCoilsDisabled",
                    $"Coils are disabled for the Modbus {runtimeRole}, but alarm '{alarmId}' {addressRole} uses Coil.");
            }

            if (address.Address >= endpoint.CoilCount)
            {
                return ModbusOperationResult.Failure(
                    "ModbusAlarmCoilRangeInvalid",
                    $"Modbus alarm '{alarmId}' {addressRole} Coil address is outside the configured {runtimeRole} coil range.");
            }

            return ModbusOperationResult.Success();
        }

        if (!endpoint.HoldingRegistersEnabled)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRegistersDisabled",
                $"Holding registers are disabled for the Modbus {runtimeRole}, but alarm '{alarmId}' {addressRole} uses HoldingRegister.");
        }

        if (address.Address >= endpoint.RegisterCount)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRegisterRangeInvalid",
                $"Modbus alarm '{alarmId}' {addressRole} Holding Register address is outside the configured {runtimeRole} register range.");
        }

        return ModbusOperationResult.Success();
    }

    private static ModbusOperationResult ValidateRegisterValueRange(
        ModbusAlarmOptions alarm,
        ModbusEndpointOptions endpoint,
        string runtimeRole)
    {
        if (!alarm.RegisterValueEnabled)
        {
            return ModbusOperationResult.Success();
        }

        if (!endpoint.HoldingRegistersEnabled)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRegisterValueRegistersDisabled",
                $"Holding registers are disabled for the Modbus {runtimeRole}, but alarm '{alarm.Id}' displays a register value.");
        }

        if (alarm.RegisterValueAddress >= endpoint.RegisterCount)
        {
            return ModbusOperationResult.Failure(
                "ModbusAlarmRegisterValueRangeInvalid",
                $"Modbus alarm '{alarm.Id}' register value address is outside the configured {runtimeRole} register range.");
        }

        return ModbusOperationResult.Success();
    }

    private static bool SameAddress(
        ModbusBitAddressOptions left,
        ModbusBitAddressOptions right)
        => left.Area == right.Area
           && left.Address == right.Address
           && left.BitIndex == right.BitIndex;
}
