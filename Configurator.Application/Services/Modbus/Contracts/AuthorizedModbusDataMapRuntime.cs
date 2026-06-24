using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Application.Services.Modbus.Contracts;

public sealed class AuthorizedModbusDataMapRuntime : IModbusDataMapRuntime
{
    private readonly IModbusDataMapRuntime _inner;
    private readonly IAccessDecisionService _accessDecisionService;

    public AuthorizedModbusDataMapRuntime(
        IModbusDataMapRuntime inner,
        IAccessDecisionService accessDecisionService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
    }

    public ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap)
    {
        var decision = _accessDecisionService.Authorize(
            new AccessRequirement(Permission.ConfigureModbus, LicenseFeature.EngineeringTools));
        return decision.Succeeded
            ? _inner.ApplyDataMap(dataMap)
            : ModbusOperationResult.Failure(
                "PermissionDenied",
                "Configure Modbus permission is required.",
                decision.ReasonCode);
    }
}
