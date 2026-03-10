using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class HousingManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockObjectId = Mock.Of<IObjectIdManager>();
        var mockFaction = Mock.Of<IFactionManager>();
        var mockLocale = Mock.Of<ILocalizationManager>();
        var mockWorld = Mock.Of<IWorldManager>();
        var mockTask = Mock.Of<ITaskManager>();
        var mockSkill = Mock.Of<ISkillManager>();
        var mockHousingId = Mock.Of<IHousingIdManager>();
        var mockHousingTld = Mock.Of<IHousingTldManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockMail = Mock.Of<IMailManager>();
        var mockName = Mock.Of<INameManager>();
        var mockZone = Mock.Of<IZoneManager>();
        var mockDoodad = Mock.Of<IDoodadManager>();
        var mockUcc = Mock.Of<IUccManager>();

        var manager = new HousingManager(
            mockObjectId.Object,
            mockFaction.Object,
            mockLocale.Object,
            mockWorld.Object,
            mockTask.Object,
            mockSkill.Object,
            mockHousingId.Object,
            mockHousingTld.Object,
            mockItem.Object,
            mockMail.Object,
            mockName.Object,
            mockZone.Object,
            mockDoodad.Object,
            mockUcc.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockObjectId);
        Mock.VerifyNoOtherCalls(mockFaction);
        Mock.VerifyNoOtherCalls(mockLocale);
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockTask);
        Mock.VerifyNoOtherCalls(mockSkill);
        Mock.VerifyNoOtherCalls(mockHousingId);
        Mock.VerifyNoOtherCalls(mockHousingTld);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockMail);
        Mock.VerifyNoOtherCalls(mockName);
        Mock.VerifyNoOtherCalls(mockZone);
        Mock.VerifyNoOtherCalls(mockDoodad);
        Mock.VerifyNoOtherCalls(mockUcc);
    }
}