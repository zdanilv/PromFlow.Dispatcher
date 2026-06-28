using Configurator.Application.Services;
using Xunit;

namespace Configurator.Tests.Unit;

public sealed class ApplicationOptionsTests
{
    [Fact]
    public void Defaults_to_admin_mode()
    {
        var options = new ApplicationOptions();

        Assert.True(options.IsAdminMode);
        Assert.False(options.IsUserMode);
        Assert.Equal(ApplicationOptions.AdminWorkMode, options.NormalizedWorkMode);
    }

    [Theory]
    [InlineData("admin", true, false, "admin")]
    [InlineData("ADMIN", true, false, "admin")]
    [InlineData("user", false, true, "user")]
    [InlineData(" User ", false, true, "user")]
    [InlineData("unknown", true, false, "admin")]
    [InlineData("", true, false, "admin")]
    public void Normalizes_work_mode(string workMode, bool isAdmin, bool isUser, string normalized)
    {
        var options = new ApplicationOptions { WorkMode = workMode };

        Assert.Equal(isAdmin, options.IsAdminMode);
        Assert.Equal(isUser, options.IsUserMode);
        Assert.Equal(normalized, options.NormalizedWorkMode);
    }
}
