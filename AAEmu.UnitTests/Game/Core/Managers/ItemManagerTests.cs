using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ItemManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockSkill = new Mock<ISkillManager>();
        var mockItemId = new Mock<IItemIdManager>();
        var mockContainerId = new Mock<IContainerIdManager>();
        var mockLocale = new Mock<ILocalizationManager>();
        var mockTask = new Mock<ITaskManager>();
        var mockWorld = new Mock<IWorldManager>();
        var manager = new ItemManager(mockSkill.Object, mockItemId.Object, mockContainerId.Object, mockLocale.Object, mockTask.Object, mockWorld.Object);

        Assert.NotNull(manager);
        mockSkill.VerifyNoOtherCalls();
        mockItemId.VerifyNoOtherCalls();
        mockContainerId.VerifyNoOtherCalls();
        mockLocale.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
    }
}
