using System.Linq;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActObjAbilityLevel : QuestActTemplate
    {
        public byte AbilityId { get; set; }
        public byte Level { get; set; }
        public bool UseAlias { get; set; }
        public uint QuestActObjAliasId { get; set; }

        public override bool Use(ICharacter character, Quest quest, int objective)
        {
            _log.Warn("QuestActObjAbilityLevel");

            var completes = new List<bool>();
            for (var i = 1; i < 11; i++)
            {
                completes.Add(false);
            }

            for (var i = 0; i < 10; i++)
            {
                var ability = character.Abilities.Abilities[(AbilityType)i + 1];
                int abLevel = ExpirienceManager.Instance.GetLevelFromExp(ability.Exp);
                completes[i] = abLevel >= Level;
            }

            return completes.All(a => a == true);
        }
    }
}
