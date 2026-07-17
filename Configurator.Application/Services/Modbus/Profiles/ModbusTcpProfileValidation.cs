using Configurator.Application.Services.Modbus.Configuration;

namespace Configurator.Application.Services.Modbus.Profiles;

/// <summary>
/// Результат предварительной проверки импортируемого профиля.
/// </summary>
public sealed class ModbusTcpProfileValidation
{
    public ModbusTcpProfile? Profile { get; init; }

    public string? ErrorMessage { get; init; }

    public ModbusRangeCorrectionProposal RangeCorrection { get; init; } = ModbusRangeCorrectionProposal.None;

    public bool Succeeded => Profile is not null && string.IsNullOrWhiteSpace(ErrorMessage);

    public static ModbusTcpProfileValidation Failure(string message) => new() { ErrorMessage = message };
}

/// <summary>
/// Изменения endpoint, необходимые для размещения всех точек профиля.
/// </summary>
public sealed class ModbusRangeCorrectionProposal
{
    public static ModbusRangeCorrectionProposal None { get; } = new();

    public int ClientCoilCount { get; init; }

    public int ClientRegisterCount { get; init; }

    public int ServerCoilCount { get; init; }

    public int ServerRegisterCount { get; init; }

    public bool EnableClientCoils { get; init; }

    public bool EnableClientRegisters { get; init; }

    public bool EnableServerCoils { get; init; }

    public bool EnableServerRegisters { get; init; }

    public bool HasChanges { get; init; }

    public ModbusTcpProfile Apply(ModbusTcpProfile source)
    {
        var result = source.Clone();
        ApplyEndpoint(result.Runtime.Client, ClientCoilCount, ClientRegisterCount, EnableClientCoils, EnableClientRegisters);
        ApplyEndpoint(result.Runtime.Server, ServerCoilCount, ServerRegisterCount, EnableServerCoils, EnableServerRegisters);
        return result;
    }

    public string ToConfirmationMessage()
    {
        if (!HasChanges)
        {
            return string.Empty;
        }

        return $"Импортируемые связи выходят за настроенные диапазоны Modbus.\n\n" +
               $"Клиент (Client / Master): Coils {ClientCoilCount}, Holding Registers {ClientRegisterCount}.\n" +
               $"Сервер (Server / Slave): Coils {ServerCoilCount}, Holding Registers {ServerRegisterCount}.\n\n" +
               "Исправить настройки Modbus и продолжить импорт?";
    }

    private static void ApplyEndpoint(
        ModbusEndpointOptions endpoint,
        int coilCount,
        int registerCount,
        bool enableCoils,
        bool enableRegisters)
    {
        endpoint.CoilCount = coilCount;
        endpoint.RegisterCount = registerCount;
        if (enableCoils)
        {
            endpoint.CoilsEnabled = true;
        }

        if (enableRegisters)
        {
            endpoint.HoldingRegistersEnabled = true;
        }
    }
}
