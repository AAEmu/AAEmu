using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Items.Procs;

/// <summary>
/// Fills <see cref="ItemProcTemplate.SkillTemplate"/> from each row's <c>skill_id</c>. The bind has to
/// run after both <c>item_procs</c> and the skill catalog are loaded: the boot orchestrator finishes
/// <c>SkillManager.Load</c> (and its <c>OnSkillsLoaded</c> event) before <c>ItemManager</c> has
/// subscribed, so an event-only bind leaves every proc without a skill and combat procs never fire.
/// </summary>
public static class ItemProcSkillBindRules
{
    /// <summary>
    /// Resolves each proc's skill and returns how many skill ids were missing from the catalog.
    /// </summary>
    public static int Bind(IEnumerable<ItemProcTemplate> procs, Func<uint, SkillTemplate> resolveSkill)
    {
        ArgumentNullException.ThrowIfNull(procs);
        ArgumentNullException.ThrowIfNull(resolveSkill);

        var missing = 0;
        foreach (var proc in procs)
        {
            if (proc == null)
                continue;

            proc.SkillTemplate = resolveSkill(proc.SkillId);
            if (proc.SkillTemplate == null)
                missing++;
        }

        return missing;
    }
}
