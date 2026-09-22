using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Reward: adds point to the character's actability actability_group_id. quest_act_supply_actabilities
/// has 633 rows; 614 sit on exploration quests (category 79) giving 50 to 500 points of group 34,
/// the rest on festival, salt trade and anniversary quests over groups 1, 3, 4, 5, 12, 13, 20, 31
/// and 33. The client reader LoadQuestActSupplyActabilityDescs (x2game-dev.dll FUN_39d45c40) reads
/// id, actability_group_id, point. CharacterActability.AddPoint applies the expert-limit cap of the
/// current step; the client learns of the change through the labor delta packet ChangeLaborCore
/// sends, here with a zero labor change. The content amount is granted as is, without the labor
/// path's ActabilityRate.
/// </summary>
public class QuestActSupplyActability(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ActabilityGroupId { get; set; }
    public int Point { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), ActabilityGroupId {ActabilityGroupId}, Point {Point}");
        var point = QuestSupplyPointRules.Grant(Point);
        if (point <= 0 || quest.Owner is not Character player)
            return true;

        if (!player.Actability.Actabilities.TryGetValue(ActabilityGroupId, out var actability))
        {
            Logger.Warn("{0}({1}): quest {2} names actability group {3}, which {4} does not have",
                QuestActTemplateName, DetailId, quest.TemplateId, ActabilityGroupId, player.Name);
            return true;
        }

        var applied = player.Actability.AddPoint(ActabilityGroupId, point);
        if (applied != 0)
            player.SendPacket(new SCCharacterLaborPowerChangedPacket(0, 0, 0, ActabilityGroupId, applied, actability.Step));
        return true;
    }
}
