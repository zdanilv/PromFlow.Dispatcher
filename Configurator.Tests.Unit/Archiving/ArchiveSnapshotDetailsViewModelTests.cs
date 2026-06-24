using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Desktop.Workspace.Archive;
using Xunit;

namespace Configurator.Tests.Unit.Archiving;

public sealed class ArchiveSnapshotDetailsViewModelTests
{
    [Fact]
    public void Load_SlicesRegistersAndFormatsHexBits()
    {
        var viewModel = new ArchiveSnapshotDetailsViewModel
        {
            Area = ArchiveSnapshotArea.HoldingRegisters,
            DisplayMode = ArchiveValueDisplayMode.Hex,
            Count = 2
        };
        viewModel.Load(Snapshot());
        viewModel.StartAddress = 401;

        Assert.Equal([401, 402], viewModel.Values.Select(row => row.Address).ToArray());
        Assert.Equal(["0x00FF", "0x8001"], viewModel.Values.Select(row => row.DisplayValue).ToArray());

        viewModel.DisplayMode = ArchiveValueDisplayMode.Bits;

        Assert.Equal("0000000011111111", viewModel.Values[0].DisplayValue);
        Assert.Equal("1000000000000001", viewModel.Values[1].DisplayValue);
    }

    [Fact]
    public void Load_OutOfRangeSliceReturnsEmptyValuesWithoutThrowing()
    {
        var viewModel = new ArchiveSnapshotDetailsViewModel { Count = 4 };

        viewModel.Load(Snapshot());
        viewModel.StartAddress = 9999;

        Assert.Empty(viewModel.Values);
        Assert.Equal("No values in selected slice.", viewModel.Summary);
    }

    private static RawModbusSnapshotArchiveRecord Snapshot()
        => new(
            Guid.NewGuid(),
            "device-1",
            ModbusRuntimeRole.Client,
            1,
            DateTimeOffset.UtcNow,
            coilStartAddress: 10,
            holdingRegisterStartAddress: 400,
            [true, false, true],
            [1, 0x00FF, 0x8001],
            "hash",
            ArchiveResolution.HighResolution,
            1);
}
