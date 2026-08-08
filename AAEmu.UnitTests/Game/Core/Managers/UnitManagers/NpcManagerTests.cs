using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class NpcManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockModel = Mock.Of<IModelManager>();
        var mockFaction = Mock.Of<IFactionManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockTask = Mock.Of<ITaskManager>();
        var manager = new NpcManager(
            mockObjId.Object,
            mockModel.Object,
            mockFaction.Object,
            mockItem.Object,
            mockTask.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockObjId);
        Mock.VerifyNoOtherCalls(mockModel);
        Mock.VerifyNoOtherCalls(mockFaction);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockTask);
    }
}
