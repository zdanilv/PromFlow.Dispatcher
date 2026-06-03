using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;

namespace Configurator.Application.Services.OpcUa.Tags;

/// <summary>
/// Хранит значение OPC UA тега вместе с адресом, временем получения и статусом качества.
/// </summary>
public sealed record OpcUaTagValue(
    OpcUaTagAddress Address,
    object? Value,
    DateTimeOffset Timestamp,
    string Status);
