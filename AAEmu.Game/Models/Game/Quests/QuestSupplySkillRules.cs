namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// quest_act_supply_skills (2 rows, each in a Ready component next to a QuestActConReportNpc:
/// quest 9002 skill 38440, quest 10452 skill 46452). Ready components OR their acts, so the supply
/// must not complete the turn-in on its own: it fires once the report act has completed the
/// component, and only once per quest instance. Both skills are hidden (skills.show f) and
/// auto_learn t, so teaching them would change nothing; 38440 consumes 1000 labor into actability
/// group 5 and 46452 applies buff 28588, a 20 second notice, which only has an effect when the
/// skill is cast on the character at the report.
/// </summary>
public static class QuestSupplySkillRules
{
    public static bool ShouldCast(bool reportGateMet, bool alreadyCast, uint skillId)
        => reportGateMet && !alreadyCast && skillId != 0;
}
