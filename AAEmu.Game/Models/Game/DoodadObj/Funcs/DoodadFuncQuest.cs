using AAEmu.Game.Core.Managers;
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
        Logger.Trace("DoodadFuncQuest : skillId {0}, QuestKindId {1}, QuestId {2}", skillId, QuestKindId, QuestId);

        if (caster is Character character)
        {
            if (!character.Quests.HasQuest(QuestId))
            {
                character.Quests.Add(QuestId);
            }
            else
            {
                //character.Quests.OnReportToDoodad(owner.ObjId, QuestId, 0);
                // инициируем событие
                //Task.Run(() => QuestManager.Instance.DoReportEvents(character, QuestId, 0, owner.TemplateId, 0));
                QuestManager.Instance.DoReportEvents(character, QuestId, 0, owner.ObjId, 0);
            }
        }
    }
}
