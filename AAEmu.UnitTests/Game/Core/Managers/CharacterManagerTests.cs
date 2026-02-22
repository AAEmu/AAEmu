using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CharacterManagerTests
{
    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        var mockWorld = new Mock<IWorldManager>();
        var mockAccount = new Mock<IAccountManager>();
        var mockName = new Mock<INameManager>();
        var mockCharId = new Mock<ICharacterIdManager>();
        var mockFaction = new Mock<IFactionManager>();
        var mockSkill = new Mock<ISkillManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockFamily = new Mock<IFamilyManager>();
        var mockMail = new Mock<IMailManager>();
        var mockTask = new Mock<ITaskManager>();

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

        Assert.NotNull(manager);
        mockWorld.VerifyNoOtherCalls();
        mockAccount.VerifyNoOtherCalls();
        mockName.VerifyNoOtherCalls();
        mockCharId.VerifyNoOtherCalls();
        mockFaction.VerifyNoOtherCalls();
        mockSkill.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
        mockHousing.VerifyNoOtherCalls();
        mockFamily.VerifyNoOtherCalls();
        mockMail.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
    }
}
