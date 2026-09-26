using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Families;

namespace AAEmu.UnitTests.Game.Models.Game;

public class FamilyProgressionActionTests
{
    [Test]
    public async Task FamilyServerPolicy_UsesFirstRoleAssignmentAndTruncatedDepartureLoss()
    {
        await Assert.That(FamilyProgressionRules.CanChangeRole(0, 100, 604800)).IsTrue();
        await Assert.That(FamilyProgressionRules.CanChangeRole(100, 604899, 604800)).IsFalse();
        await Assert.That(FamilyProgressionRules.CanChangeRole(100, 604900, 604800)).IsTrue();
        await Assert.That(FamilyProgressionRules.ApplyDepartureExperienceLoss(99, 10)).IsEqualTo(90u);
        await Assert.That(FamilyProgressionRules.IsNewUtcDay(0, 1)).IsTrue();
        await Assert.That(FamilyProgressionRules.IsNewUtcDay(1, 86399)).IsFalse();
        await Assert.That(FamilyProgressionRules.IsNewUtcDay(86399, 86400)).IsTrue();
    }

    [Test]
    public async Task FamilyTextAdministration_UsesUtf8ByteLimits()
    {
        await Assert.That(FamilyProgressionRules.IsWithinFamilyNameByteLimit(new string('a', 256))).IsTrue();
        await Assert.That(FamilyProgressionRules.IsWithinFamilyNameByteLimit(new string('a', 257))).IsFalse();
        await Assert.That(FamilyProgressionRules.IsValidNotice(new string('a', 800))).IsTrue();
        await Assert.That(FamilyProgressionRules.IsValidNotice(new string('a', 801))).IsFalse();
        await Assert.That(FamilyProgressionRules.IsWithinFamilyNameByteLimit(new string('\u00E9', 128))).IsTrue();
        await Assert.That(FamilyProgressionRules.IsWithinFamilyNameByteLimit(new string('\u00E9', 129))).IsFalse();
        await Assert.That(FamilyProgressionRules.IsValidNotice(new string('\u00E9', 400))).IsTrue();
        await Assert.That(FamilyProgressionRules.IsValidNotice(new string('\u00E9', 401))).IsFalse();
        await Assert.That(FamilyProgressionRules.IsValidNotice(null)).IsFalse();
    }

    [Test]
    public async Task ExpeditionQuestReward_ForwardsConfiguredPointToNormalManagerPath()
    {
        var character = new Character(new UnitCustomModelParams());
        var grants = new List<uint>();
        var action = new QuestActSupplyExpeditionExp(
            new QuestComponentTemplate(new QuestTemplate()))
        {
            Point = 275,
            AddExp = (_, point) =>
            {
                grants.Add(point);
                return true;
            }
        };

        var quest = new Quest(
            null,
            character,
            Mock.Of<IQuestManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IExpressTextManager>().Object,
            Mock.Of<IWorldManager>().Object);
        var completed = action.RunAct(quest, null, 0);

        await Assert.That(completed).IsTrue();
        await Assert.That(grants).IsEquivalentTo([275u]);
    }

    [Test]
    public async Task FamilyLevelChange_UsesValueOneAsTargetAndRejectsUnknownOperands()
    {
        var character = new Character(new UnitCustomModelParams());
        var targets = new List<uint>();
        var action = new FamilyLevelChange
        {
            TryLevelUp = (_, level) =>
            {
                targets.Add(level);
                return true;
            }
        };

        action.Execute(character, null, null, null, null, null, null, default, 2, 0, 0, 0);
        action.Execute(character, null, null, null, null, null, null, default, 3, 1, 0, 0);

        await Assert.That(targets).IsEquivalentTo([2u]);
    }
}
