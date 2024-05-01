using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncQuest : DoodadFuncTemplate
{
    // doodad_funcs
    public uint QuestKindId { get; set; }
    public uint QuestId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace($"DoodadFuncQuest : skillId {skillId}, QuestKindId {QuestKindId}, QuestId {QuestId}");

        if (caster is Character character)
        {
            if (!character.Quests.HasQuest(QuestId))
            {
                if (caster is Character player)
                    player.SendPacket(new SCDoodadQuestAcceptPacket(owner.ObjId, QuestId));
                // character.Quests.AddQuestFromDoodad(QuestId, owner.ObjId);
            }
            else
            {
                QuestManager.Instance.DoReportEvents(character, QuestId, 0, owner.ObjId, 0);
            }
        }
    }
}
