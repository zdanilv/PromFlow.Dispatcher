using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Xunit;

namespace Configurator.Infrastructure.OpcUa.Tests;

public sealed class OpcUaTagWriteRequestValidatorTests
{
    private readonly OpcUaTagWriteRequestValidator _validator = new();

    [Theory]
    [InlineData(true)]
    [InlineData(123)]
    [InlineData(1.25d)]
    [InlineData("text")]
    public void Validate_ShouldAcceptSupportedScalarTypes(object value)
    {
        var result = _validator.Validate(new OpcUaTagWriteRequest(Address(), value));

        Assert.True(result.Succeeded, result.Error?.Message);
    }

    [Fact]
    public void Validate_ShouldRejectNullValue()
    {
        var result = _validator.Validate(new OpcUaTagWriteRequest(Address(), null));

        Assert.False(result.Succeeded);
        Assert.Equal("TagValueEmpty", result.Error?.Code);
    }

    [Fact]
    public void Validate_ShouldRejectUnsupportedValueType()
    {
        var result = _validator.Validate(new OpcUaTagWriteRequest(Address(), DateTimeOffset.Now));

        Assert.False(result.Succeeded);
        Assert.Equal("TagValueUnsupported", result.Error?.Code);
    }

    [Fact]
    public void Validate_ShouldRejectEmptyAddress()
    {
        var result = _validator.Validate(new OpcUaTagWriteRequest(new OpcUaTagAddress("", ""), true));

        Assert.False(result.Succeeded);
        Assert.Equal("TagAddressEmpty", result.Error?.Code);
    }

    private static OpcUaTagAddress Address()
        => new("urn:test", "DemoDevice/Commands/RequestedBool", "Boolean");
}

public sealed class OpcUaTagConfigurationValidatorTests
{
    private readonly OpcUaTagConfigurationValidator _validator = new();

    [Fact]
    public void ValidateTag_ShouldRejectEmptyAddress()
    {
        var result = _validator.ValidateTag(new OpcUaConfiguredTag
        {
            Name = "Empty",
            Address = new OpcUaTagAddress("", "", "Boolean")
        });

        Assert.False(result.Succeeded);
        Assert.Equal("TagAddressEmpty", result.Error?.Code);
    }

    [Fact]
    public void ValidateTag_ShouldRejectUnsupportedDataType()
    {
        var result = _validator.ValidateTag(new OpcUaConfiguredTag
        {
            Name = "Object",
            Address = new OpcUaTagAddress("urn:test", "Device/Object", "Object")
        });

        Assert.False(result.Succeeded);
        Assert.Equal("TagDataTypeUnsupported", result.Error?.Code);
    }

    [Fact]
    public void ValidateLists_ShouldRejectDuplicateAddresses()
    {
        var first = Tag("First", "Device/Tag");
        var second = Tag("Second", "Device/Tag");

        var result = _validator.ValidateLists([first, second], []);

        Assert.False(result.Succeeded);
        Assert.Equal("TagDuplicate", result.Error?.Code);
    }

    private static OpcUaConfiguredTag Tag(string name, string identifier)
        => new()
        {
            Name = name,
            Address = new OpcUaTagAddress("urn:test", identifier, "String"),
            Access = OpcUaTagAccess.Read
        };
}
