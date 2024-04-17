using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjDistance(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public bool WithIn { get; set; }
    public uint NpcId { get; set; }
    public int Distance { get; set; }
    public uint HighlightDoodadId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    /// <summary>
    /// Checks if target Npc is within range (or not) of the player
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActObjDistance({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), NpcId {NpcId}, Distance {Distance}, WithIn {WithIn}");

        var res = CalculateObjective((GameObject)quest.Owner);
        SetObjective(quest, res);
        return res > 0;
    }

    /// <summary>
    /// Helper function to check if the Npc is within (or outside) defined range
    /// </summary>
    /// <param name="owner"></param>
    /// <returns></returns>
    private int CalculateObjective(GameObject owner)
    {
        var npcs = WorldManager.GetAround<Npc>(owner, Distance, true);
        var obj = 0;
        if (WithIn)
        {
            // Within Range?
            foreach (var npc in npcs)
            {
                if (npc.TemplateId == NpcId)
                {
                    obj = 1;
                    break;
                }
            }
        }
        else
        {
            // Outside of Range?
            obj = 1;
            foreach (var npc in npcs)
            {
                if (npc.TemplateId == NpcId)
                {
                    obj = 0;
                    break;
                }
            }
        }
        return obj;
    }

    // TODO: Add event trackers for movements?
}
