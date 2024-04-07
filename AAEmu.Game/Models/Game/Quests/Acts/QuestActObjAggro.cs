using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjAggro(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public int Range { get; set; }
    public int Rank1 { get; set; }
    public int Rank2 { get; set; }
    public int Rank3 { get; set; }
    public int Rank1Ratio { get; set; }
    public int Rank2Ratio { get; set; }
    public int Rank3Ratio { get; set; }
    public bool Rank1Item { get; set; }
    public bool Rank2Item { get; set; }
    public bool Rank3Item { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        // TODO: Implement Aggro ranking system to pick rewards
        Logger.Debug("QuestActObjAggro");
        return true;
    }

    /// <summary>
    /// Returns true if the objective has been met after killing target Npc. Objective count will be set to Rank number
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActObjAggro({DetailId}).RunAct: Quest: {quest.TemplateId}, Range {Range}, Rank1 {Rank1} ");
        return GetObjective(quest) > 0;
    }

    public override void Initialize(Quest quest, IQuestAct questAct)
    {
        base.Initialize(quest, questAct);
        quest.Owner.Events.OnKill += OnKill;
    }

    public override void DeInitialize(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnKill -= OnKill;
        base.DeInitialize(quest, questAct);
    }

    /// <summary>
    /// Set objective count based on Aggro ranking when the Npc gets killed
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnKill(object sender, OnKillArgs e)
    {
        // Check if it's the correct Npc for this quest
        if ((e.target is Npc npc) && (e.OwningQuest.QuestAcceptorType == QuestAcceptorType.Npc) &&
            (npc.TemplateId == e.OwningQuest.AcceptorId))
        {
            var aggroRate = npc.GetAggroRatingInPercent(e.OwningQuest.Owner.ObjId);

            // Handle ranking and defaults
            e.OwningQuest.AllowItemRewards = false;
            e.OwningQuest.QuestRewardRatio = 0.0;

            // Rank 1
            if (aggroRate <= Rank1)
            {
                SetObjective(e.OwningQuest, 1);
                e.OwningQuest.QuestRewardRatio = Rank1Ratio / 100.0;
                e.OwningQuest.AllowItemRewards = Rank1Item;
                return;
            }

            // Rank 2
            if (aggroRate <= Rank2)
            {
                SetObjective(e.OwningQuest, 2);
                e.OwningQuest.QuestRewardRatio = Rank2Ratio / 100.0;
                e.OwningQuest.AllowItemRewards = Rank2Item;
                return;
            }

            // Rank 3
            if (aggroRate <= Rank3)
            {
                SetObjective(e.OwningQuest, 3);
                e.OwningQuest.QuestRewardRatio = Rank3Ratio / 100.0;
                e.OwningQuest.AllowItemRewards = Rank3Item;
            }
        }
    }
}
