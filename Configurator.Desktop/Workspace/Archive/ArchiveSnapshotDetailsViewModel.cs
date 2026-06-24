using System.Collections.ObjectModel;
using System.Globalization;
using Configurator.Application.Services.Archiving;
using Configurator.Desktop;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Archive;

public sealed class ArchiveSnapshotDetailsViewModel : ViewModelBase
{
    private RawModbusSnapshotArchiveRecord? _snapshot;
    private ArchiveSnapshotArea _area = ArchiveSnapshotArea.HoldingRegisters;
    private ArchiveValueDisplayMode _displayMode = ArchiveValueDisplayMode.Decimal;
    private int _startAddress;
    private int _count = 32;
    private string _summary = "Select a snapshot to load details.";

    public ArchiveSnapshotArea Area
    {
        get => _area;
        set
        {
            this.RaiseAndSetIfChanged(ref _area, value);
            RefreshValues();
        }
    }

    public ArchiveValueDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _displayMode, value);
            RefreshValues();
        }
    }

    public int StartAddress
    {
        get => _startAddress;
        set
        {
            this.RaiseAndSetIfChanged(ref _startAddress, value);
            RefreshValues();
        }
    }

    public int Count
    {
        get => _count;
        set
        {
            this.RaiseAndSetIfChanged(ref _count, Math.Max(0, value));
            RefreshValues();
        }
    }

    public string Summary
    {
        get => _summary;
        private set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    public ObservableCollection<ArchiveSnapshotValueRow> Values { get; } = [];

    public void Load(RawModbusSnapshotArchiveRecord snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        StartAddress = Area == ArchiveSnapshotArea.Coils
            ? snapshot.CoilStartAddress
            : snapshot.HoldingRegisterStartAddress;
        RefreshValues();
    }

    public void Clear()
    {
        _snapshot = null;
        Values.Clear();
        Summary = "Select a snapshot to load details.";
    }

    private void RefreshValues()
    {
        Values.Clear();
        if (_snapshot is null)
        {
            Summary = "Select a snapshot to load details.";
            return;
        }

        if (Count == 0)
        {
            Summary = "No values in selected slice.";
            return;
        }

        var added = Area == ArchiveSnapshotArea.Coils
            ? AddCoils(_snapshot)
            : AddRegisters(_snapshot);
        Summary = added == 0
            ? "No values in selected slice."
            : $"{added.ToString(CultureInfo.InvariantCulture)} values from {_snapshot.CapturedAtUtc:u}.";
    }

    private int AddCoils(RawModbusSnapshotArchiveRecord snapshot)
    {
        var added = 0;
        var first = Math.Max(StartAddress, snapshot.CoilStartAddress);
        var lastExclusive = Math.Min(
            StartAddress + Count,
            snapshot.CoilStartAddress + snapshot.Coils.Count);
        for (var address = first; address < lastExclusive; address++)
        {
            var value = snapshot.Coils[address - snapshot.CoilStartAddress];
            Values.Add(new ArchiveSnapshotValueRow(
                address,
                value ? "1" : "0",
                value ? "true" : "false"));
            added++;
        }

        return added;
    }

    private int AddRegisters(RawModbusSnapshotArchiveRecord snapshot)
    {
        var added = 0;
        var first = Math.Max(StartAddress, snapshot.HoldingRegisterStartAddress);
        var lastExclusive = Math.Min(
            StartAddress + Count,
            snapshot.HoldingRegisterStartAddress + snapshot.HoldingRegisters.Count);
        for (var address = first; address < lastExclusive; address++)
        {
            var value = snapshot.HoldingRegisters[address - snapshot.HoldingRegisterStartAddress];
            Values.Add(new ArchiveSnapshotValueRow(
                address,
                FormatRegister(value),
                value.ToString(CultureInfo.InvariantCulture)));
            added++;
        }

        return added;
    }

    private string FormatRegister(ushort value)
        => DisplayMode switch
        {
            ArchiveValueDisplayMode.Hex => "0x" + value.ToString("X4", CultureInfo.InvariantCulture),
            ArchiveValueDisplayMode.Bits => Convert.ToString(value, 2).PadLeft(16, '0'),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
}

public sealed record ArchiveSnapshotValueRow(
    int Address,
    string DisplayValue,
    string RawValue);
