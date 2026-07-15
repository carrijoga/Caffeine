using Caffeine.Core.Common;
using Xunit;

namespace Caffeine.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() =>
        Assert.NotNull(typeof(IAppTimer).Assembly);
}
