using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjAbilityLevel : QuestActTemplate
{
    public AbilityType AbilityId { get; set; }
    public byte Level { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug($"QuestActObjAbilityLevel, AbilityId: {AbilityId}, Level: {Level}");

        var completes = new List<bool>();
        for (var i = AbilityType.General+1; i < AbilityType.None; i++)
        {
            // If AbilityId is set, only check that one, otherwise, check all skill trees
            if ((AbilityId > 0) && (AbilityId != i))
                continue;

            var ability = character.Abilities.Abilities[i];
            int abLevel = ExperienceManager.Instance.GetLevelFromExp(ability.Exp);
            completes.Add(abLevel >= Level);
        }

        return completes.All(a => a == true);
    }
}
