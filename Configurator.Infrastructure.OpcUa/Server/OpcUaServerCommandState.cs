using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Configurator.Infrastructure.OpcUa.Common;
using Microsoft.Extensions.Logging;

namespace Configurator.Infrastructure.OpcUa.Server;

/// <summary>
/// Хранит значения command-тегов, записанных во встроенный OPC UA сервер внешними клиентами.
/// </summary>
internal sealed class OpcUaServerCommandState : IOpcUaServerCommandStateProvider, IOpcUaServerCommandSink
{
    private readonly ObservableValue<OpcUaTagValue> _commands = new();
    private readonly ILogger<OpcUaServerCommandState> _logger;
    private readonly object _lock = new();
    private OpcUaTagValue? _lastCommand;

    /// <summary>
    /// Создает хранилище command-состояния и подключает журналирование входящих записей.
    /// </summary>
    public OpcUaServerCommandState(ILogger<OpcUaServerCommandState> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Последний command-тег, записанный внешним клиентом во встроенный сервер.
    /// </summary>
    public OpcUaTagValue? LastCommand
    {
        get
        {
            lock (_lock)
            {
                return _lastCommand;
            }
        }
    }

    /// <summary>
    /// Поток command-тегов, которые поступают из callback записи OPC UA SDK.
    /// </summary>
    public IObservable<OpcUaTagValue> CommandChanges => _commands;

    /// <summary>
    /// Сохраняет command-тег, полученный от SDK, и публикует его подписчикам runtime.
    /// </summary>
    public void PublishCommand(OpcUaTagValue command)
    {
        lock (_lock)
        {
            _lastCommand = command;
        }

        _logger.LogInformation(
            "OPC UA server received command {Identifier} = {Value}.",
            command.Address.Identifier,
            command.Value);

        try
        {
            // Копия нужна, чтобы подписчики не зависели от потока SDK callback записи тега.
            _commands.Publish(command);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OPC UA command subscriber failed.");
        }
    }
}
