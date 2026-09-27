using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjAggro(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public override bool CountsAsAnObjective => true;
    private const int MaxRankObjective = 3;
    // This Count override is needed to allow SetObjective to allow more than a value of 1
    public override int Count => MaxRankObjective;
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

    /// <summary>
    /// Returns true if the objective has been met after killing target Npc. Objective count will be set to Rank number
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Range {Range}, Ranks {Rank1}/{Rank2}/{Rank3}");
        return currentObjectiveCount > 0;
    }

    public override void InitializeAction(Quest quest, QuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnKill += questAct.OnKill;
    }

    public override void FinalizeAction(Quest quest, QuestAct questAct)
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
    public override void OnKill(QuestAct questAct, object sender, OnKillArgs e)
    {
        if (questAct.Id != ActId)
            return;

        var q = questAct.QuestComponent.Parent.Parent;

        // Check if it's the correct Npc for this quest
        if (e.Target is Npc npc && q.QuestAcceptorType == QuestAcceptorType.Npc && npc.TemplateId == q.AcceptorId)
        {
            var aggroRate = npc.GetAggroRatingInPercent(q.Owner.ObjId);

            // Handle ranking and defaults
            q.AllowItemRewards = false;
            q.QuestRewardRatio = 0.0;

            // rank1/rank2/rank3 are percentile cut-offs; the objective count carries the band
            // that was reached, so a quest that paid nothing stays unfulfilled.
            var band = QuestAggroRankRules.Band(aggroRate, Rank1, Rank2, Rank3);
            if (band == 0)
            {
                Logger.Warn($"{QuestActTemplateName}({DetailId}).OnKill: Quest: {q.TemplateId}, aggro {aggroRate:F1} is past the lowest rank {Rank3}, no rank reward, Player {q.Owner.Name} ({q.Owner.Id})");
                return;
            }

            SetObjective(q, band);
            var (ratio, itemAllowed) = band switch
            {
                1 => (Rank1Ratio, Rank1Item),
                2 => (Rank2Ratio, Rank2Item),
                _ => (Rank3Ratio, Rank3Item)
            };
            q.QuestRewardRatio = ratio / 100.0;
            q.AllowItemRewards = itemAllowed;
            Logger.Debug($"{QuestActTemplateName}({DetailId}).OnKill: Quest: {q.TemplateId}, aggro {aggroRate:F1} is Rank{band}, Player {q.Owner.Name} ({q.Owner.Id})");
            return;
        }
        Logger.Warn($"{QuestActTemplateName}({DetailId}).OnKill: Quest: {q.TemplateId}, no rank reward found, Player {q.Owner.Name} ({q.Owner.Id})");
    }
}
