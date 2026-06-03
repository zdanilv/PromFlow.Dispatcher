using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Infrastructure.OpcUa.Server;

/// <summary>
/// Принимает записи command-тегов от NodeManager и передает их в поток состояния сервера.
/// </summary>
internal interface IOpcUaServerCommandSink
{
    /// <summary>
    /// Передает запись command-тега встроенного сервера во внутренний runtime.
    /// </summary>
    void PublishCommand(OpcUaTagValue command);
}
