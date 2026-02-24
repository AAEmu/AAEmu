using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class NpcManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockObjId = new Mock<IObjectIdManager>();
        var mockModel = new Mock<IModelManager>();
        var mockFaction = new Mock<IFactionManager>();
        var mockItem = new Mock<IItemManager>();
        var mockAI = new Mock<IAIManager>();
        var manager = new NpcManager(mockObjId.Object, mockModel.Object, mockFaction.Object, mockItem.Object, mockAI.Object);

        Assert.NotNull(manager);
        mockObjId.VerifyNoOtherCalls();
        mockModel.VerifyNoOtherCalls();
        mockFaction.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
        mockAI.VerifyNoOtherCalls();
    }
}
