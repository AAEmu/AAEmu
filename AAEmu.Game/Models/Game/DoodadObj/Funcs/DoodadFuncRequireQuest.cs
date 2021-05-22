using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRequireQuest : DoodadFuncTemplate
    {
        public WorldInteractionType WorldInteractionId { get; set; }
        public uint QuestId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncRequireQuest QuestId: {0}, WorldIntId {1}", QuestId, WorldInteractionId);
            owner.ToPhaseAndUse = false;
        }
    }
}
