using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjAbilityLevel(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public AbilityType AbilityId { get; set; }
    public byte Level { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if the Ability Levels (classes) are at least the specified amounts
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), AbilityId: {AbilityId}, Level: {Level}");

        if (AbilityId > 0)
        {
            // Single Ability check
            var ability = quest.Owner.Abilities.Abilities[AbilityId];
            int abLevel = ExperienceManager.Instance.GetLevelFromExp(ability.Exp);
            return abLevel >= Level;
        }

        // All abilities check
        for (var i = AbilityType.General+1; i < AbilityType.None; i++)
        {
            var ability = quest.Owner.Abilities.Abilities[i];
            int abLevel = ExperienceManager.Instance.GetLevelFromExp(ability.Exp);
            if (abLevel < Level)
                return false; // Fail check if any of the Abilities is below the required level
        }

        // Only return true, if all included checks were true
        return true;
    }
}
