using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusAlarmMapValidatorTests
{
    private readonly ModbusAlarmMapValidator _validator = new();

    [Fact]
    public void ModbusOptionsClone_PreservesAlarmMap()
    {
        var options = CreateOptions();

        var clone = options.Clone();
        clone.AlarmMap[0].Message = "Changed";
        clone.AlarmMap[0].Alarm.Address = 3;

        Assert.Equal("Авария", options.AlarmMap[0].Message);
        Assert.Equal(0, options.AlarmMap[0].Alarm.Address);
    }

    [Fact]
    public void Validate_AcceptsValidAlarmMap()
    {
        var result = _validator.Validate(CreateOptions(), ModbusRunMode.Client);

        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        var options = CreateOptions();
        options.AlarmMap.Add(options.AlarmMap[0].Clone());

        var result = _validator.Validate(options, ModbusRunMode.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusAlarmDuplicate", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsSameAlarmAndAcknowledgementBit()
    {
        var options = CreateOptions();
        options.AlarmMap[0].Acknowledgement = options.AlarmMap[0].Alarm.Clone();

        var result = _validator.Validate(options, ModbusRunMode.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusAlarmAcknowledgementAddressConflict", result.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(16)]
    public void Validate_RejectsInvalidRegisterBit(int? bitIndex)
    {
        var options = CreateOptions();
        options.AlarmMap[0].Alarm = new ModbusBitAddressOptions
        {
            Area = ModbusDataArea.HoldingRegister,
            Address = 0,
            BitIndex = bitIndex
        };

        var result = _validator.Validate(options, ModbusRunMode.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusAlarmRegisterBitInvalid", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsOutOfEndpointRangeAddress()
    {
        var options = CreateOptions();
        options.Client.CoilCount = 1;
        options.AlarmMap[0].Acknowledgement.Address = 2;

        var result = _validator.Validate(options, ModbusRunMode.Client);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusAlarmCoilRangeInvalid", result.ErrorCode);
    }

    [Theory]
    [InlineData(999, "ModbusAlarmRepeatIntervalInvalid")]
    [InlineData(86400001, "ModbusAlarmRepeatIntervalInvalid")]
    public void Validate_RejectsInvalidRepeatInterval(int repeatIntervalMs, string expectedCode)
    {
        var options = CreateOptions();
        options.AlarmMap[0].RepeatIntervalMs = repeatIntervalMs;

        var result = _validator.Validate(options, ModbusRunMode.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    private static ModbusOptions CreateOptions()
        => new()
        {
            Client = new ModbusEndpointOptions
            {
                CoilCount = 4,
                RegisterCount = 4,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true
            },
            Server = new ModbusEndpointOptions
            {
                CoilCount = 4,
                RegisterCount = 4,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true
            },
            AlarmMap =
            [
                new()
                {
                    Id = "alarm.main",
                    Kind = ModbusAlarmKind.Fault,
                    Message = "Авария",
                    Alarm = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.Coil,
                        Address = 0
                    },
                    Acknowledgement = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.Coil,
                        Address = 2
                    },
                    RepeatIntervalMs = 1000,
                    AcknowledgementPulseDurationMs = 1
                }
            ]
        };
}
