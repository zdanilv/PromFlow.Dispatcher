using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Validation;

/// <summary>
/// Проверяет настройки OPC UA тегов до запуска runtime или импорта.
/// </summary>
public interface IOpcUaTagConfigurationValidator
{
    /// <summary>
    /// Проверяет теги на неполные адреса, дубли и несовместимый доступ.
    /// </summary>
    OpcUaOperationResult ValidateTag(OpcUaConfiguredTag tag);

    /// <summary>
    /// Возвращает ошибки конфигурации в формате, понятном для UI.
    /// </summary>
    OpcUaOperationResult ValidateLists(
        IEnumerable<OpcUaConfiguredTag> telemetryTags,
        IEnumerable<OpcUaConfiguredTag> commandTags);
}
