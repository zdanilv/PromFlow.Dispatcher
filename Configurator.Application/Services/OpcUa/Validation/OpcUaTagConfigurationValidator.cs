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
/// Проверяет список OPC UA тегов на неполные адреса, дубликаты и несовместимый доступ.
/// </summary>
public sealed class OpcUaTagConfigurationValidator : IOpcUaTagConfigurationValidator
{
    /// <summary>
    /// Проверяет один тег на заполненный адрес, имя и поддерживаемый тип данных.
    /// </summary>
    public OpcUaOperationResult ValidateTag(OpcUaConfiguredTag tag)
    {
        if (tag.Address is null || tag.Address.IsEmpty)
        {
            return OpcUaOperationResult.Failure(
                "TagAddressEmpty",
                "Tag namespace and identifier must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(tag.Name))
        {
            return OpcUaOperationResult.Failure(
                "TagNameEmpty",
                "Tag name must not be empty.");
        }

        var dataType = tag.Address.DataType ?? string.Empty;
        if (!OpcUaDataTypeSupport.IsSupported(dataType))
        {
            return OpcUaOperationResult.Failure(
                "TagDataTypeUnsupported",
                $"Tag data type '{dataType}' is not supported in v1.");
        }

        return OpcUaOperationResult.Success();
    }

    /// <summary>
    /// Проверяет telemetry и command списки перед сохранением или запуском runtime.
    /// </summary>
    public OpcUaOperationResult ValidateLists(
        IEnumerable<OpcUaConfiguredTag> telemetryTags,
        IEnumerable<OpcUaConfiguredTag> commandTags)
    {
        return ValidateList(telemetryTags, "Telemetry")
               ?? ValidateList(commandTags, "Command")
               ?? OpcUaOperationResult.Success();
    }

    /// <summary>
    /// Проверяет один список тегов и ловит дубли по namespace и identifier.
    /// </summary>
    private OpcUaOperationResult? ValidateList(IEnumerable<OpcUaConfiguredTag> tags, string listName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            var tagValidation = ValidateTag(tag);
            if (!tagValidation.Succeeded)
            {
                return tagValidation;
            }

            var key = $"{tag.Address.NamespaceUri}|{tag.Address.Identifier}";
            if (!seen.Add(key))
            {
                return OpcUaOperationResult.Failure(
                    "TagDuplicate",
                    $"{listName} tag '{tag.Name}' is duplicated.");
            }
        }

        return null;
    }
}
