namespace Sway.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() => Assert.NotNull(typeof(SmokeTests).Assembly);
}
