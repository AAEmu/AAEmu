using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class MusicManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockMusicId = new Mock<IMusicIdManager>();
        var mockItem = new Mock<IItemManager>();
        var manager = new MusicManager(mockMusicId.Object, mockItem.Object);

        await Assert.That(manager).IsNotNull();
        mockMusicId.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
    }
}