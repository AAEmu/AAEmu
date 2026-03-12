using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class MusicManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockMusicId = Mock.Of<IMusicIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var manager = new MusicManager(mockMusicId.Object, mockItem.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockMusicId);
        Mock.VerifyNoOtherCalls(mockItem);
    }
}