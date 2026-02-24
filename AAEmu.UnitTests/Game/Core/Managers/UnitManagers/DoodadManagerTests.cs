using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class DoodadManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        Assert.NotNull(manager);
        mockObjId.VerifyNoOtherCalls();
        mockDoodadId.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
        mockHousing.VerifyNoOtherCalls();
        mockSus.VerifyNoOtherCalls();
    }
}
