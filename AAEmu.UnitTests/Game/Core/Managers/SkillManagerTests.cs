using AAEmu.Game.Core.Managers;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SkillManagerTests
{
    /// <summary>
    /// Verifies SkillManager can be constructed with injected deps.
    /// IAnimationManager and IPlotManager are only called during Load() which
    /// requires a SQLite DB — covered by integration tests.
    /// </summary>
    [Fact]
    public void Constructor_WithMockedDependencies_DoesNotThrow()
    {
        var mockAnimation = new Mock<IAnimationManager>();
        var mockPlot = new Mock<IPlotManager>();

        var manager = new SkillManager(mockAnimation.Object, mockPlot.Object);

        Assert.NotNull(manager);
        mockAnimation.VerifyNoOtherCalls();
        mockPlot.VerifyNoOtherCalls();
    }
}
