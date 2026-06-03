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
/// Формирует идентификатор пользователя для подключения к OPC UA endpoint.
/// </summary>
public interface IOpcUaIdentityProvider
{
    /// <summary>
    /// Создает идентификатор пользователя по настройкам аутентификации.
    /// </summary>
    OpcUaIdentityDescriptor GetIdentity(OpcUaOptions? options = null);
}
