using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Runtime;

namespace Configurator.Infrastructure.Modbus.Archiving;

public sealed class ModbusConfigurationFingerprint
{
    public string Compute(ModbusOptions options, ModbusRuntimeRole role)
    {
        ArgumentNullException.ThrowIfNull(options);

        var endpoint = role switch
        {
            ModbusRuntimeRole.Client => options.Client,
            ModbusRuntimeRole.Server => options.Server,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Only client and server roles can be archived.")
        };

        var canonical = BuildCanonicalText(options, endpoint, role);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string BuildCanonicalText(
        ModbusOptions options,
        ModbusEndpointOptions endpoint,
        ModbusRuntimeRole role)
    {
        var builder = new StringBuilder();

        Append(builder, "role", role.ToString());
        Append(builder, "endpoint.host", endpoint.Host);
        Append(builder, "endpoint.bindAddress", endpoint.BindAddress);
        Append(builder, "endpoint.port", endpoint.Port);
        Append(builder, "endpoint.unitId", endpoint.UnitId);
        Append(builder, "endpoint.enabled", endpoint.Enabled);
        Append(builder, "endpoint.pollIntervalMs", endpoint.PollIntervalMs);
        Append(builder, "endpoint.coilsEnabled", endpoint.CoilsEnabled);
        Append(builder, "endpoint.holdingRegistersEnabled", endpoint.HoldingRegistersEnabled);
        Append(builder, "endpoint.coilStartAddress", endpoint.CoilStartAddress);
        Append(builder, "endpoint.holdingRegisterStartAddress", endpoint.HoldingRegisterStartAddress);
        Append(builder, "endpoint.coilCount", endpoint.CoilCount);
        Append(builder, "endpoint.registerCount", endpoint.RegisterCount);
        Append(builder, "writeConfirmationTimeoutMs", options.WriteConfirmationTimeoutMs);

        foreach (var point in options.DataMap
            .OrderBy(point => point.Name, StringComparer.Ordinal)
            .ThenBy(point => point.Area)
            .ThenBy(point => point.Address)
            .ThenBy(point => point.Length))
        {
            Append(builder, "datamap.name", point.Name);
            Append(builder, "datamap.area", point.Area.ToString());
            Append(builder, "datamap.address", point.Address);
            Append(builder, "datamap.length", point.Length);
            Append(builder, "datamap.bitIndex", point.BitIndex);
            Append(builder, "datamap.type", point.Type.ToString());
            Append(builder, "datamap.access", point.Access.ToString());
            Append(builder, "datamap.writeMode", point.WriteMode.ToString());
            Append(builder, "datamap.pulseDurationMs", point.PulseDurationMs);
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, string name, string? value)
        => builder
            .Append(name)
            .Append('=')
            .Append(value ?? string.Empty)
            .Append('\n');

    private static void Append(StringBuilder builder, string name, int value)
        => Append(builder, name, value.ToString(CultureInfo.InvariantCulture));

    private static void Append(StringBuilder builder, string name, int? value)
        => Append(builder, name, value?.ToString(CultureInfo.InvariantCulture));

    private static void Append(StringBuilder builder, string name, bool value)
        => Append(builder, name, value ? "true" : "false");
}
