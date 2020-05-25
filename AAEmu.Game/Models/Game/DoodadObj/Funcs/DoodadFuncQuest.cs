using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncQuest : DoodadFuncTemplate
    {
        public uint QuestKindId { get; set; }
        public uint QuestId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncQuest : skillId {0}, QuestKindId {1}, QuestId {2}", skillId, QuestKindId, QuestId);

            if (caster is Character character)
            {
                character.Quests.Add(QuestId);
            }
        }
    }
}
