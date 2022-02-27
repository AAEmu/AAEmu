using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRequireQuest : DoodadPhaseFuncTemplate
    {
        public WorldInteractionType WorldInteractionId { get; set; }
        public uint QuestId { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncRequireQuest QuestId: {0}, WorldIntId {1}", QuestId, WorldInteractionId);

            //if (caster is Character character)
            //{
            //    character.Quests.OnInteraction(WorldInteractionId, character.CurrentTarget);
            //}
            return false;
        }
    }
}
