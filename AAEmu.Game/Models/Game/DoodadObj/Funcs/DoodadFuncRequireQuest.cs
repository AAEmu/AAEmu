using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRequireQuest : DoodadPhaseFuncTemplate
{
    public WorldInteractionType WorldInteractionId { get; set; }
    public uint QuestId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncRequireQuest QuestId: {0}, WorldIntId {1}", QuestId, WorldInteractionId);

        if (caster is Character character)
        {
            //character.Quests.OnInteraction(WorldInteractionId, character.CurrentTarget);
            if (character.Quests.HasQuest(QuestId))
                return false; // This player is on the correct quest, continue
            return true; // Player doesn't have the quest, stop execution
        }
        return false; // caster is not a player, allow execution as we can't check
    }
}
