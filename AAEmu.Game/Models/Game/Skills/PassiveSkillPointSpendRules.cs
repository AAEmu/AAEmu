using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which <c>passive_buffs</c> rows charge the shared combat skill-point pool.
/// </summary>
/// <remarks>
/// System actives (<c>active='t'</c> and <c>req_points &lt;= 0</c>) are server-granted quest /
/// event markers (the only shipped costing example is the shipwreck treasure start row). Their
/// <c>skill_points</c> column is not spent by the client <c>GetSkillPoint</c> used total, so the
/// server must not charge them either — otherwise LearnSkill sees remaining 0 while the UI still
/// shows 1 point left.
/// Click-learned general / tree passives with a real cost still count.
/// </remarks>
public static class PassiveSkillPointSpendRules
{
    /// <summary>
    /// Points this passive adds to <c>GetUsedSkillPoints</c> / the learn budget check.
    /// </summary>
    public static int PoolCost(PassiveBuffTemplate template)
    {
        if (template == null || template.SkillPoints <= 0)
            return 0;

        // Always-on system actives: grant the buff, never spend the combat pool.
        if (template.Active && template.ReqPoints <= 0)
            return 0;

        return template.SkillPoints;
    }
}
