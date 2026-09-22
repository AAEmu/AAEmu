using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestSendMailRulesTests
{
    // quest_act_obj_send_mails 1 (test quest 8952, Start): item1 34820 x1, item2 and item3 empty.
    [Test]
    public async Task Row1_MailsTheOneExistingItem()
    {
        var attachments = QuestSendMailRules.Attachments([(34820u, 1), (0u, 0), (0u, 0)], itemId => itemId == 34820);

        await Assert.That(attachments.Count).IsEqualTo(1);
        await Assert.That(attachments[0].TemplateId).IsEqualTo(34820u);
        await Assert.That(attachments[0].Count).IsEqualTo(1);
    }

    // Rows 2 and 3 (test quests 9004 and 150) name item 45395, which has no items row.
    [Test]
    public async Task Rows2And3_DropTheMissingItem()
    {
        var attachments = QuestSendMailRules.Attachments([(45395u, 1), (0u, 0), (0u, 0)], _ => false);

        await Assert.That(attachments.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NonPositiveCounts_AreDropped()
    {
        var attachments = QuestSendMailRules.Attachments([(34820u, 0), (34820u, -1), (34820u, 2)], _ => true);

        await Assert.That(attachments.Count).IsEqualTo(1);
        await Assert.That(attachments[0].Count).IsEqualTo(2);
    }

    [Test]
    public async Task MissingInput_MailsNothing()
    {
        await Assert.That(QuestSendMailRules.Attachments(null, _ => true).Count).IsEqualTo(0);
        await Assert.That(QuestSendMailRules.Attachments([(34820u, 1)], null).Count).IsEqualTo(0);
    }
}
