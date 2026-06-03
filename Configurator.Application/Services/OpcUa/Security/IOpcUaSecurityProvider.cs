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
/// Формирует параметры безопасности OPC UA клиента и сервера из настроек приложения.
/// </summary>
public interface IOpcUaSecurityProvider
{
    /// <summary>
    /// Создает режим безопасности по настройкам приложения.
    /// </summary>
    OpcUaSecurityDescriptor GetSecurity(OpcUaOptions? options = null);
}
