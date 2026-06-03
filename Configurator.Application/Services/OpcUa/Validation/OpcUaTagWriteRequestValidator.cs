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
/// Проверяет запрос записи OPC UA тега на валидный адрес, доступ и значение.
/// </summary>
public sealed class OpcUaTagWriteRequestValidator : IOpcUaTagWriteRequestValidator
{
    /// <summary>
    /// Проверяет модель на ошибки конфигурации и возвращает список диагностических сообщений.
    /// </summary>
    public OpcUaOperationResult Validate(OpcUaTagWriteRequest request)
    {
        if (request.Address is null || request.Address.IsEmpty)
        {
            return OpcUaOperationResult.Failure(
                "TagAddressEmpty",
                "Tag address must not be empty.");
        }

        if (request.Value is null)
        {
            return OpcUaOperationResult.Failure(
                "TagValueEmpty",
                "Tag value must not be null.");
        }

        if (request.Value is not bool
            and not sbyte
            and not byte
            and not short
            and not ushort
            and not int
            and not uint
            and not long
            and not ulong
            and not float
            and not double
            and not string)
        {
            return OpcUaOperationResult.Failure(
                "TagValueUnsupported",
                $"Tag value type '{request.Value.GetType().Name}' is not supported in v1.");
        }

        return OpcUaOperationResult.Success();
    }
}
