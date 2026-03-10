using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using Moq;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class HousingManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockObjectId = new Mock<IObjectIdManager>();
        var mockFaction = new Mock<IFactionManager>();
        var mockLocale = new Mock<ILocalizationManager>();
        var mockWorld = new Mock<IWorldManager>();
        var mockTask = new Mock<ITaskManager>();
        var mockSkill = new Mock<ISkillManager>();
        var mockHousingId = new Mock<IHousingIdManager>();
        var mockHousingTld = new Mock<IHousingTldManager>();
        var mockItem = new Mock<IItemManager>();
        var mockMail = new Mock<IMailManager>();
        var mockName = new Mock<INameManager>();
        var mockZone = new Mock<IZoneManager>();
        var mockDoodad = new Mock<IDoodadManager>();
        var mockUcc = new Mock<IUccManager>();

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
        mockObjectId.VerifyNoOtherCalls();
        mockFaction.VerifyNoOtherCalls();
        mockLocale.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
        mockSkill.VerifyNoOtherCalls();
        mockHousingId.VerifyNoOtherCalls();
        mockHousingTld.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
        mockMail.VerifyNoOtherCalls();
        mockName.VerifyNoOtherCalls();
        mockZone.VerifyNoOtherCalls();
        mockDoodad.VerifyNoOtherCalls();
        mockUcc.VerifyNoOtherCalls();
    }
}