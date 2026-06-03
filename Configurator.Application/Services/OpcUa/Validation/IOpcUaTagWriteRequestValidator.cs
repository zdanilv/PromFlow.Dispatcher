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
/// Проверяет запрос записи OPC UA тега перед обращением к SDK.
/// </summary>
public interface IOpcUaTagWriteRequestValidator
{
    /// <summary>
    /// Возвращает ошибку, если адрес и значение не проходят правила записи.
    /// </summary>
    OpcUaOperationResult Validate(OpcUaTagWriteRequest request);
}
