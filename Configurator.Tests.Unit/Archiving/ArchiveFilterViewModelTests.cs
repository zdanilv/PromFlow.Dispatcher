using Configurator.Application.Services.Archiving;
using Configurator.Desktop.Workspace.Archive;
using Xunit;

namespace Configurator.Tests.Unit.Archiving;

public sealed class ArchiveFilterViewModelTests
{
    [Fact]
    public void CreateQuery_ConvertsLocalInputToUtcAndClampsPageSize()
    {
        var viewModel = new ArchiveFilterViewModel(maxPageSize: 50)
        {
            FromLocalText = "2026-01-02 03:04",
            ToLocalText = "2026-01-02 04:04",
            PageSize = 500,
            DeviceId = " device-1 ",
            SignalId = " system.emergency ",
            Outcome = nameof(EquipmentCommandAuditResult.Succeeded)
        };

        var query = viewModel.CreateQuery(2, ArchiveRecordKind.EquipmentCommandAudit);

        Assert.Equal(ExpectedUtc(2026, 1, 2, 3, 4), query.FromUtc);
        Assert.Equal(ExpectedUtc(2026, 1, 2, 4, 4), query.ToUtc);
        Assert.Equal(2, query.PageNumber);
        Assert.Equal(50, query.PageSize);
        Assert.Equal("device-1", query.DeviceId);
        Assert.Equal("system.emergency", query.SignalId);
        var outcome = query switch { { Result: var value } => value };
        Assert.Equal(nameof(EquipmentCommandAuditResult.Succeeded), outcome);
    }

    [Fact]
    public void CreateQuery_InvalidRangeThrowsBeforeServiceCall()
    {
        var viewModel = new ArchiveFilterViewModel(maxPageSize: 100)
        {
            FromLocalText = "2026-01-02 05:00",
            ToLocalText = "2026-01-02 04:00"
        };

        Assert.Throws<InvalidOperationException>(() => viewModel.CreateQuery(1));
    }

    [Fact]
    public void CreateBoundedExportQuery_RequiresBothDates()
    {
        var viewModel = new ArchiveFilterViewModel(maxPageSize: 100)
        {
            FromLocalText = string.Empty,
            ToLocalText = "2026-01-02 04:00"
        };

        Assert.Throws<InvalidOperationException>(() => viewModel.CreateBoundedExportQuery());
    }

    private static DateTimeOffset ExpectedUtc(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }
}
