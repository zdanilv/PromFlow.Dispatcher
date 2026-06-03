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
/// Предоставляет поток состояний OPC UA подключения для runtime и UI.
/// </summary>
public interface IOpcUaConnectionStateProvider
{
    /// <summary>
    /// Возвращает последнее опубликованное состояние подключения.
    /// </summary>
    OpcUaConnectionState Current { get; }

    /// <summary>
    /// Наблюдаемый поток изменений состояния подключения.
    /// </summary>
    IObservable<OpcUaConnectionState> ConnectionStates { get; }
}
