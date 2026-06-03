using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Contracts;

/// <summary>
/// Предоставляет поток command-значений, записанных внешними OPC UA клиентами во встроенный сервер.
/// </summary>
public interface IOpcUaServerCommandStateProvider
{
    /// <summary>
    /// Последнее command-значение, принятое сервером.
    /// </summary>
    OpcUaTagValue? LastCommand { get; }

    /// <summary>
    /// Поток изменений command-тегов сервера.
    /// </summary>
    IObservable<OpcUaTagValue> CommandChanges { get; }
}
