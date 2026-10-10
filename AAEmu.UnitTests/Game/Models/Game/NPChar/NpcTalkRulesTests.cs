using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// The cast that means "the player talked to this NPC". Picking the talk entry on an NPC's interaction bar
/// makes the client cast this skill at the NPC; the server reads that cast as the talk and advances the
/// quest objective. Nothing else may be mistaken for it.
/// </summary>
public class NpcTalkRulesTests
{
    [Test]
    public async Task NpcTalkSkill_IsATalk()
    {
        await Assert.That(NpcTalkRules.IsTalkSkill(SkillsEnum.NpcTalk)).IsTrue();
    }

    [Test]
    public async Task OtherSkills_AreNotTalk()
    {
        await Assert.That(NpcTalkRules.IsTalkSkill(0u)).IsFalse();
        await Assert.That(NpcTalkRules.IsTalkSkill(2u)).IsFalse();
        await Assert.That(NpcTalkRules.IsTalkSkill(13876u)).IsFalse();
        await Assert.That(NpcTalkRules.IsTalkSkill(13878u)).IsFalse();
    }
}
