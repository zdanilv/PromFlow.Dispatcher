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
/// Описывает запись значения в OPC UA тег через клиентскую роль.
/// </summary>
public interface IOpcUaTagWriter
{
    /// <summary>
    /// Записывает значение в тег и возвращает результат, удобный для SDK-адаптера.
    /// </summary>
    /// <param name="request">Адрес тега и значение, которое нужно записать.</param>
    /// <param name="cancellationToken">Токен отмены операции записи.</param>
    /// <returns>Результат записи с диагностикой ошибки SDK или валидации.</returns>
    Task<OpcUaOperationResult> WriteTagAsync(
        OpcUaTagWriteRequest request,
        CancellationToken cancellationToken = default);
}
