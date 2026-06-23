using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Infrastructure.Modbus.Archiving;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests.Archiving;

public sealed class ModbusConfigurationFingerprintTests
{
    [Fact]
    public void Fingerprint_SameOptions_ProducesStableLowercaseSha256()
    {
        var fingerprint = new ModbusConfigurationFingerprint();
        var options = CreateOptions();

        var first = fingerprint.Compute(options, ModbusRuntimeRole.Client);
        var second = fingerprint.Compute(options.Clone(), ModbusRuntimeRole.Client);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void Fingerprint_ShuffledDataMap_ProducesSameHash()
    {
        var fingerprint = new ModbusConfigurationFingerprint();
        var options = CreateOptions();
        var shuffled = options.Clone();
        shuffled.DataMap = [shuffled.DataMap[1], shuffled.DataMap[0]];

        var first = fingerprint.Compute(options, ModbusRuntimeRole.Client);
        var second = fingerprint.Compute(shuffled, ModbusRuntimeRole.Client);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fingerprint_EndpointStartAddressOrDataMapChange_ChangesHash()
    {
        var fingerprint = new ModbusConfigurationFingerprint();
        var baseline = CreateOptions();
        var baselineHash = fingerprint.Compute(baseline, ModbusRuntimeRole.Client);

        var changedEndpoint = baseline.Clone();
        changedEndpoint.Client.CoilStartAddress++;
        var changedDataMap = baseline.Clone();
        changedDataMap.DataMap[0].PulseDurationMs++;

        Assert.NotEqual(baselineHash, fingerprint.Compute(changedEndpoint, ModbusRuntimeRole.Client));
        Assert.NotEqual(baselineHash, fingerprint.Compute(changedDataMap, ModbusRuntimeRole.Client));
    }

    private static ModbusOptions CreateOptions()
        => new()
        {
            Client = new ModbusEndpointOptions
            {
                Host = "10.0.0.10",
                Port = 1502,
                UnitId = 7,
                PollIntervalMs = 250,
                CoilStartAddress = 10,
                HoldingRegisterStartAddress = 20,
                CoilCount = 16,
                RegisterCount = 32
            },
            Server = new ModbusEndpointOptions
            {
                BindAddress = "0.0.0.0",
                Port = 1503,
                UnitId = 8,
                PollIntervalMs = 300,
                CoilStartAddress = 100,
                HoldingRegisterStartAddress = 200,
                CoilCount = 8,
                RegisterCount = 12
            },
            WriteConfirmationTimeoutMs = 1234,
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "PumpRun",
                    Area = ModbusDataArea.Coil,
                    Address = 3,
                    Length = 1,
                    Access = ModbusDataAccess.ReadWrite,
                    Type = ModbusValueType.Bool,
                    WriteMode = ModbusWriteMode.Pulse,
                    PulseDurationMs = 500
                },
                new ModbusDataPointOptions
                {
                    Name = "TankLevel",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 10,
                    Length = 2,
                    Access = ModbusDataAccess.Read,
                    Type = ModbusValueType.Real,
                    BitIndex = 2,
                    WriteMode = ModbusWriteMode.Latched
                }
            ]
        };
}
