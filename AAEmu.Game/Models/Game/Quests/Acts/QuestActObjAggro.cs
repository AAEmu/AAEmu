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
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActObjAggro({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Range {Range}, Ranks {Rank1}/{Rank2}/{Rank3}");
        return currentObjectiveCount > 0;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnKill += questAct.OnKill;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnKill -= questAct.OnKill;
        base.FinalizeAction(quest, questAct);
    }

    /// <summary>
    /// Set objective count based on Aggro ranking when the Npc gets killed
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    public override void OnKill(IQuestAct questAct, object sender, OnKillArgs e)
    {
        if (questAct.Id != ActId)
            return;

        var q = questAct.QuestComponent.Parent.Parent;

        // Check if it's the correct Npc for this quest
        if ((e.target is Npc npc) && (q.QuestAcceptorType == QuestAcceptorType.Npc) && (npc.TemplateId == q.AcceptorId))
        {
            var aggroRate = npc.GetAggroRatingInPercent(q.Owner.ObjId);

            // Handle ranking and defaults
            q.AllowItemRewards = false;
            q.QuestRewardRatio = 0.0;

            // Rank 1
            if (aggroRate <= Rank1)
            {
                SetObjective(q, 1);
                q.QuestRewardRatio = Rank1Ratio / 100.0;
                q.AllowItemRewards = Rank1Item;
                Logger.Debug($"QuestActObjAggro({DetailId}).OnKill: Quest: {q.TemplateId}, Rank1 reward, Player {q.Owner.Name} ({q.Owner.Id})");
                return;
            }

            // Rank 2
            if (aggroRate <= Rank2)
            {
                SetObjective(q, 2);
                q.QuestRewardRatio = Rank2Ratio / 100.0;
                q.AllowItemRewards = Rank2Item;
                Logger.Debug($"QuestActObjAggro({DetailId}).OnKill: Quest: {q.TemplateId}, Rank1 reward, Player {q.Owner.Name} ({q.Owner.Id})");
                return;
            }

            // Rank 3
            if (aggroRate <= Rank3)
            {
                SetObjective(q, 3);
                q.QuestRewardRatio = Rank3Ratio / 100.0;
                q.AllowItemRewards = Rank3Item;
                Logger.Debug($"QuestActObjAggro({DetailId}).OnKill: Quest: {q.TemplateId}, Rank1 reward, Player {q.Owner.Name} ({q.Owner.Id})");
                return;
            }
        }
        Logger.Warn($"QuestActObjAggro({DetailId}).OnKill: Quest: {q.TemplateId}, no rank reward found, Player {q.Owner.Name} ({q.Owner.Id})");
    }
}
