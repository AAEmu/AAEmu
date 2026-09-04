using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CashShopManagerTests
{
    [Test]
    public void DisableShop_CallsGetAllCharacters()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        mockWorld.GetAllCharacters().Returns([]);
        var manager = new CashShopManager(mockWorld.Object, Mock.Of<IAccountManager>().Object, Mock.Of<ILocalizationManager>().Object, Mock.Of<IItemManager>().Object);
        manager.DisableShop();

        mockWorld.GetAllCharacters().WasCalled(Times.Once);
    }
}