using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.Models.Game.Mails;

public class MailAttachmentLoadRulesTests
{
    [Test]
    public async Task CanReload_MailHeldItem_IsTrue()
    {
        var item = new Item(1) { SlotType = SlotType.Mail, Count = 4 };
        await Assert.That(MailAttachmentLoadRules.CanReload(item)).IsTrue();
    }

    [Test]
    public async Task CanReload_ClaimedInventoryItem_IsFalse()
    {
        var item = new Item(1) { SlotType = SlotType.Inventory, Count = 4 };
        await Assert.That(MailAttachmentLoadRules.CanReload(item)).IsFalse();
    }

    [Test]
    public async Task CanReload_MissingOrEmpty_IsFalse()
    {
        await Assert.That(MailAttachmentLoadRules.CanReload(null)).IsFalse();
        await Assert.That(MailAttachmentLoadRules.CanReload(new Item(1) { SlotType = SlotType.Mail, Count = 0 }))
            .IsFalse();
    }
}
