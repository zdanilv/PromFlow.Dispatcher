using Configurator.Application.Services.Archiving;
using Xunit;

namespace Configurator.Tests.Unit.Archiving;

public sealed class ArchiveOptionsValidatorTests
{
    private readonly ArchiveOptionsValidator _validator = new();

    [Fact]
    public void Validate_DefaultDisabledOptions_Succeeds()
    {
        var result = _validator.Validate(new ArchiveOptions());

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EnabledValidOptions_Succeeds()
    {
        var result = _validator.Validate(CreateValidOptions());

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EnabledMissingDeviceId_RejectsArchiveDeviceIdRequired()
    {
        var options = CreateValidOptions();
        options.DeviceId = " ";

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ArchiveDeviceIdRequired");
    }

    [Fact]
    public void Validate_IpOnlyDeviceId_RejectsArchiveDeviceIdInvalid()
    {
        var options = CreateValidOptions();
        options.DeviceId = "127.0.0.1";

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ArchiveDeviceIdInvalid");
    }

    [Fact]
    public void Validate_DeviceIdWithPathSeparator_RejectsArchiveDeviceIdInvalid()
    {
        var options = CreateValidOptions();
        options.DeviceId = "plant\\line";

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ArchiveDeviceIdInvalid");
    }

    [Theory]
    [InlineData(nameof(ArchiveOptions.LongTermSnapshotIntervalMs))]
    [InlineData(nameof(ArchiveOptions.BatchFlushIntervalMs))]
    [InlineData(nameof(ArchiveOptions.BusyTimeoutMs))]
    public void Validate_InvalidIntervals_RejectsArchiveIntervalInvalid(string property)
    {
        var options = CreateValidOptions();
        SetIntProperty(options, property, 0);

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Code == "ArchiveIntervalInvalid" && error.PropertyName == property);
    }

    [Theory]
    [InlineData(nameof(ArchiveOptions.ChannelCapacity), 0)]
    [InlineData(nameof(ArchiveOptions.BatchSize), 0)]
    [InlineData(nameof(ArchiveOptions.BatchSize), 10001)]
    public void Validate_InvalidCapacityOrBatch_RejectsArchiveCapacityInvalid(
        string property,
        int value)
    {
        var options = CreateValidOptions();
        SetIntProperty(options, property, value);

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Code == "ArchiveCapacityInvalid" && error.PropertyName == property);
    }

    [Theory]
    [InlineData(nameof(ArchiveOptions.HighResolutionRetentionHours))]
    [InlineData(nameof(ArchiveOptions.LongTermRetentionDays))]
    public void Validate_InvalidRetention_RejectsArchiveRetentionInvalid(string property)
    {
        var options = CreateValidOptions();
        SetIntProperty(options, property, 0);

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Code == "ArchiveRetentionInvalid" && error.PropertyName == property);
    }

    [Fact]
    public void Validate_InvalidPathCharacters_RejectsArchivePathInvalid()
    {
        var options = CreateValidOptions();
        options.BaseDirectory = "bad|path";

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Code == "ArchivePathInvalid"
            && error.PropertyName == nameof(ArchiveOptions.BaseDirectory));
    }

    [Fact]
    public void Validate_UnsupportedPartitionMode_RejectsArchivePartitionModeUnsupported()
    {
        var options = CreateValidOptions();
        options.PartitionMode = (ArchivePartitionMode)999;

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ArchivePartitionModeUnsupported");
    }

    [Fact]
    public void Validate_UnsupportedCommandAuditFailureMode_RejectsArchiveCommandAuditFailureModeUnsupported()
    {
        var options = CreateValidOptions();
        options.CommandAuditFailureMode = (CommandAuditFailureMode)999;

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ArchiveCommandAuditFailureModeUnsupported");
    }

    [Fact]
    public void Validate_InvalidCommandAuditTimeout_RejectsArchiveIntervalInvalid()
    {
        var options = CreateValidOptions();
        options.CommandAuditEnqueueTimeoutMs = 0;

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error =>
            error.Code == "ArchiveIntervalInvalid"
            && error.PropertyName == nameof(ArchiveOptions.CommandAuditEnqueueTimeoutMs));
    }

    [Fact]
    public void Validate_EmptyEmergencySignalIds_RejectsArchiveEmergencySignalIdsRequired()
    {
        var options = CreateValidOptions();
        options.EmergencySignalIds.Clear();

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ArchiveEmergencySignalIdsRequired");
    }

    [Fact]
    public void Validate_BlankEmergencySignalId_RejectsArchiveEmergencySignalIdsInvalid()
    {
        var options = CreateValidOptions();
        options.EmergencySignalIds = ["system.emergency", " "];

        var result = _validator.Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "ArchiveEmergencySignalIdsInvalid");
    }

    private static ArchiveOptions CreateValidOptions()
        => new()
        {
            Enabled = true,
            DeviceId = "concrete-distributor-01"
        };

    private static void SetIntProperty(ArchiveOptions options, string property, int value)
    {
        typeof(ArchiveOptions)
            .GetProperty(property)!
            .SetValue(options, value);
    }
}
