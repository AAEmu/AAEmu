using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

/// <summary>
/// Start or Reward: mails item1..item3 x count1..count3 to the character through the quest reward
/// mail (MailManager.CreateQuestRewardMails, the letter DistributeRewards uses when the bag is
/// full). QuestSendMailRules drops empty slots and items with no items row; with nothing left no
/// mail is sent and the act still passes, since all three enabled rows are on test quests.
/// </summary>
public class QuestActObjSendMail(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId1 { get; set; }
    public int Count1 { get; set; }
    public uint ItemId2 { get; set; }
    public int Count2 { get; set; }
    public uint ItemId3 { get; set; }
    public int Count3 { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool RunAct(Quest quest, QuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Items {ItemId1}x{Count1} {ItemId2}x{Count2} {ItemId3}x{Count3}");
        if (quest.Owner is not Character player)
            return false;

        var attachments = QuestSendMailRules.Attachments(
            [(ItemId1, Count1), (ItemId2, Count2), (ItemId3, Count3)],
            itemId => ItemManager.Instance.GetTemplate(itemId) != null);
        if (attachments.Count == 0)
        {
            Logger.Warn("{0}({1}): quest {2} has no deliverable item ({3}, {4}, {5}), no mail sent to {6}",
                QuestActTemplateName, DetailId, quest.TemplateId, ItemId1, ItemId2, ItemId3, player.Name);
            return true;
        }

        var sent = true;
        foreach (var mail in MailManager.Instance.CreateQuestRewardMails(player, quest, attachments, 0))
            sent &= mail.Send();
        if (!sent)
            Logger.Warn("{0}({1}): quest {2} could not mail its items to {3}", QuestActTemplateName, DetailId, quest.TemplateId, player.Name);
        return sent;
    }
}
