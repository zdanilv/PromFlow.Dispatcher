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
/// Возвращает анонимный идентификатор OPC UA клиента для текущего режима безопасности.
/// </summary>
public sealed class AnonymousOpcUaIdentityProvider : IOpcUaIdentityProvider
{
    /// <summary>
    /// Возвращает анонимную identity, соответствующую режиму Anonymous.
    /// </summary>
    public OpcUaIdentityDescriptor GetIdentity(OpcUaOptions? options = null)
    {
        var current = options ?? new OpcUaOptions();
        return new OpcUaIdentityDescriptor(current.Security.Authentication.Mode);
    }
}
