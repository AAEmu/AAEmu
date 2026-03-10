using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CashShopManagerTests
{
    [Test]
    public void DisableShop_CallsGetAllCharacters()
    {
        var mockWorld = new Mock<IWorldManager>();
        mockWorld.Setup(w => w.GetAllCharacters()).Returns([]);
        var manager = new CashShopManager(mockWorld.Object, new Mock<IAccountManager>().Object, new Mock<ILocalizationManager>().Object);
        manager.DisableShop();

        mockWorld.Verify(w => w.GetAllCharacters(), Times.Once);
    }
}