using Configurator.Application.Services.Modbus.Profiles;

namespace Configurator.Desktop.Workspace.ModbusProfile;

public interface IModbusTcpProfileTransferService
{
    event EventHandler? ProfileApplied;

    Task ExportAsync(string path, CancellationToken cancellationToken = default);

    Task<ModbusTcpProfileValidation> ReadAndValidateAsync(string path, CancellationToken cancellationToken = default);

    Task<ModbusTcpProfileApplyResult> ApplyAsync(
        ModbusTcpProfile profile,
        bool applyRangeCorrection,
        CancellationToken cancellationToken = default);
}

public sealed class ModbusTcpProfileApplyResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }

    public string? WarningMessage { get; init; }

    public static ModbusTcpProfileApplyResult Success(string? warningMessage = null)
        => new() { Succeeded = true, WarningMessage = warningMessage };

    public static ModbusTcpProfileApplyResult Failure(string message)
        => new() { ErrorMessage = message };
}
