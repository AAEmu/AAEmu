using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class MusicManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockMusicId = new Mock<IMusicIdManager>();
        var mockItem = new Mock<IItemManager>();
        var manager = new MusicManager(mockMusicId.Object, mockItem.Object);

        Assert.NotNull(manager);
        mockMusicId.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
    }
}
