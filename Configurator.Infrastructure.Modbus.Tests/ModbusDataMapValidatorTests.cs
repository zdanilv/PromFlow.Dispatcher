using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed class ModbusDataMapValidatorTests
{
    private readonly ModbusDataMapValidator _validator = new();

    [Fact]
    public void Validate_AcceptsValidMap()
    {
        var result = _validator.Validate(CreateOptions(), ModbusRunMode.Server);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_RejectsDuplicateNames()
    {
        var options = CreateOptions();
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "DemoInput",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Type = ModbusValueType.UInt16,
            Access = ModbusDataAccess.Read
        });

        var result = _validator.Validate(options, ModbusRunMode.Server);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusDataPointDuplicate", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeAddressForRole()
    {
        var options = CreateOptions();
        options.Server.RegisterCount = 1;

        var result = _validator.Validate(options, ModbusRunMode.Server);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusRegisterRangeInvalid", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsWrongTypeForArea()
    {
        var options = CreateOptions();
        options.DataMap[0].Type = ModbusValueType.UInt16;

        var result = _validator.Validate(options, ModbusRunMode.Server);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusCoilTypeInvalid", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsClientDisabledArea()
    {
        var options = CreateOptions();
        options.Client.CoilsEnabled = false;

        var result = _validator.Validate(options, ModbusRunMode.Client);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusCoilsDisabled", result.ErrorCode);
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
                RegisterCount = 4
            },
            DataMap =
            [
                new()
                {
                    Name = "DemoButton",
                    Area = ModbusDataArea.Coil,
                    Address = 0,
                    Type = ModbusValueType.Bool,
                    Access = ModbusDataAccess.ReadWrite
                },
                new()
                {
                    Name = "DemoInput",
                    Area = ModbusDataArea.HoldingRegister,
                    Address = 1,
                    Type = ModbusValueType.UInt16,
                    Access = ModbusDataAccess.ReadWrite
                }
            ]
        };
}
