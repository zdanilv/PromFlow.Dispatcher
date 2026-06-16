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
    public void Validate_AcceptsModbusDemoHoldingRegisterOffsets()
    {
        var options = new ModbusOptions
        {
            Client = CreateDemoEndpoint(),
            Server = CreateDemoEndpoint(),
            DataMap =
            [
                CreateDemoPoint("Telemetry_1", 0, ModbusDataAccess.Read),
                CreateDemoPoint("Telemetry_4", 3, ModbusDataAccess.Read),
                CreateDemoPoint("Commands_1", 4, ModbusDataAccess.Write),
                CreateDemoPoint("Commands_4", 7, ModbusDataAccess.Write),
                CreateDemoPoint("MB_ТЕКУЩАЯ_ПОЗИЦИЯ", 16, ModbusDataAccess.ReadWrite),
                CreateDemoPoint("MB_ТОП_СБРОС-З_ЗАГРУЗКА", 35, ModbusDataAccess.ReadWrite)
            ]
        };

        var clientResult = _validator.Validate(options, ModbusRunMode.Client);
        var serverResult = _validator.Validate(options, ModbusRunMode.Server);

        Assert.True(clientResult.Succeeded, clientResult.ErrorMessage);
        Assert.True(serverResult.Succeeded, serverResult.ErrorMessage);
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

    [Fact]
    public void Validate_AcceptsBoolBitInsideHoldingRegister()
    {
        var options = CreateOptions();
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "route.node.bsu_1.active",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Length = 1,
            BitIndex = 7,
            Type = ModbusValueType.Bool,
            Access = ModbusDataAccess.ReadWrite
        });

        var result = _validator.Validate(options, ModbusRunMode.Server);

        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void Validate_RejectsInvalidHoldingRegisterBit(int bitIndex)
    {
        var options = CreateOptions();
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "InvalidBit",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Length = 1,
            BitIndex = bitIndex,
            Type = ModbusValueType.Bool,
            Access = ModbusDataAccess.ReadWrite
        });

        var result = _validator.Validate(options, ModbusRunMode.Server);

        Assert.False(result.Succeeded);
        Assert.Equal("ModbusRegisterBitInvalid", result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsOverlappingCoilsAndRegisterRanges()
    {
        var coilOptions = CreateOptions();
        coilOptions.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "DuplicateCoilAddress",
            Area = ModbusDataArea.Coil,
            Address = 0,
            Length = 1,
            Type = ModbusValueType.Bool,
            Access = ModbusDataAccess.Read
        });
        var registerOptions = CreateOptions();
        registerOptions.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "OverlappingRegister",
            Area = ModbusDataArea.HoldingRegister,
            Address = 1,
            Length = 1,
            Type = ModbusValueType.UInt16,
            Access = ModbusDataAccess.Read
        });

        var coilResult = _validator.Validate(coilOptions, ModbusRunMode.Server);
        var registerResult = _validator.Validate(registerOptions, ModbusRunMode.Server);

        Assert.Equal("ModbusDataPointAddressConflict", coilResult.ErrorCode);
        Assert.Equal("ModbusDataPointAddressConflict", registerResult.ErrorCode);
    }

    [Fact]
    public void Validate_AllowsDifferentBitsButRejectsWholeRegisterOverlap()
    {
        var options = CreateOptions();
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "Bit0",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Length = 1,
            BitIndex = 0,
            Type = ModbusValueType.Bool,
            Access = ModbusDataAccess.ReadWrite
        });
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "Bit1",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Length = 1,
            BitIndex = 1,
            Type = ModbusValueType.Bool,
            Access = ModbusDataAccess.ReadWrite
        });

        var valid = _validator.Validate(options, ModbusRunMode.Server);
        options.DataMap.Add(new ModbusDataPointOptions
        {
            Name = "WholeWord",
            Area = ModbusDataArea.HoldingRegister,
            Address = 2,
            Length = 1,
            Type = ModbusValueType.UInt16,
            Access = ModbusDataAccess.Read
        });
        var invalid = _validator.Validate(options, ModbusRunMode.Server);

        Assert.True(valid.Succeeded, valid.ErrorMessage);
        Assert.Equal("ModbusDataPointAddressConflict", invalid.ErrorCode);
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

    private static ModbusEndpointOptions CreateDemoEndpoint()
        => new()
        {
            CoilsEnabled = false,
            HoldingRegistersEnabled = true,
            CoilStartAddress = 0,
            HoldingRegisterStartAddress = 16384,
            CoilCount = 0,
            RegisterCount = 36
        };

    private static ModbusDataPointOptions CreateDemoPoint(
        string name,
        int address,
        ModbusDataAccess access)
        => new()
        {
            Name = name,
            Area = ModbusDataArea.HoldingRegister,
            Address = address,
            Length = 1,
            Access = access,
            Type = ModbusValueType.UInt16
        };
}
