using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// The client's "talk to NPC" action.
/// </summary>
/// <remarks>
/// An NPC the client marks as talkable gets this skill on its interaction bar, and picking it makes the
/// client cast it at the NPC. The skill carries no effects of its own — the press <em>is</em> the talk — so
/// the server has to advance the quest's talk objective when it sees the cast. Without that the objective
/// never counts, and the player is left clicking a quest NPC that appears not to respond.
/// </remarks>
public static class NpcTalkRules
{
    /// <summary>True for the cast that means "the player talked to this NPC".</summary>
    public static bool IsTalkSkill(uint skillId) => skillId == SkillsEnum.NpcTalk;
}
