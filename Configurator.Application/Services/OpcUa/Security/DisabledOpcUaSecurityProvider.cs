using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Security;

/// <summary>
/// Возвращает отключенную безопасность OPC UA для сценария None/Anonymous.
/// </summary>
public sealed class DisabledOpcUaSecurityProvider : IOpcUaSecurityProvider
{
    /// <summary>
    /// Возвращает отключенную безопасность для режима None.
    /// </summary>
    public OpcUaSecurityDescriptor GetSecurity(OpcUaOptions? options = null)
    {
        var current = options ?? new OpcUaOptions();
        return new OpcUaSecurityDescriptor(current.Security.Mode, current.Security.Certificates.Enabled);
    }
}
