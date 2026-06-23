using Configurator.Infrastructure.Persistence.Archive;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Archive;

public sealed class ArchiveFailurePolicyTests
{
    [Fact]
    public void BackoffPolicy_DoublesDelayAndCapsAtFiveSeconds()
    {
        var policy = new ArchiveBackoffPolicy();

        Assert.Equal(TimeSpan.Zero, policy.GetDelay(0));
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.GetDelay(2));
        Assert.Equal(TimeSpan.FromMilliseconds(400), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelay(20));
    }
}
