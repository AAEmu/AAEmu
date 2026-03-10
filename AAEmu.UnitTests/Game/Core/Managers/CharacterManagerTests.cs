using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CharacterManagerTests
{
    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        var mockAccount = Mock.Of<IAccountManager>();
        var mockName = Mock.Of<INameManager>();
        var mockCharId = Mock.Of<ICharacterIdManager>();
        var mockFaction = Mock.Of<IFactionManager>();
        var mockSkill = Mock.Of<ISkillManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockFamily = Mock.Of<IFamilyManager>();
        var mockMail = Mock.Of<IMailManager>();
        var mockTask = Mock.Of<ITaskManager>();

        var manager = new CharacterManager(
            mockWorld.Object,
            mockAccount.Object,
            mockName.Object,
            mockCharId.Object,
            mockFaction.Object,
            mockSkill.Object,
            mockItem.Object,
            mockHousing.Object,
            mockFamily.Object,
            mockMail.Object,
            mockTask.Object);

        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockWorld);
        Mock.VerifyNoOtherCalls(mockAccount);
        Mock.VerifyNoOtherCalls(mockName);
        Mock.VerifyNoOtherCalls(mockCharId);
        Mock.VerifyNoOtherCalls(mockFaction);
        Mock.VerifyNoOtherCalls(mockSkill);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockHousing);
        Mock.VerifyNoOtherCalls(mockFamily);
        Mock.VerifyNoOtherCalls(mockMail);
        Mock.VerifyNoOtherCalls(mockTask);
    }
}
