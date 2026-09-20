using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// Letters a craft order sends: the product to the requester, the fee after the resident charge
/// to the crafter, and the escrow back when a listing lapses. Sender names are the locale-helper
/// keys the client already ships.
/// </summary>
public sealed class MailForCraftOrder : BaseMail
{
    private MailForCraftOrder()
    {
        MailType = MailType.SysExpress;
        Header.SenderId = 0;
        Header.Status = MailStatus.Unread;
        var now = DateTime.UtcNow;
        Body.SendDate = now;
        Body.RecvDate = now;
    }

    /// <summary>The requester's letter: the crafted item, keyed as a completed order.</summary>
    public static MailForCraftOrder ForCompletedOrder(uint receiverId, string receiverName, uint craftId, Item product)
    {
        var mail = ForReceiver(receiverId, receiverName, CraftOrderProcessRules.CompletedMailSender, craftId);
        if (product != null)
        {
            product.OwnerId = receiverId;
            product.SlotType = SlotType.Mail;
            mail.Body.Attachments.Add(product);
        }

        return mail;
    }

    /// <summary>The crafter's letter: the listed fee minus the resident charge.</summary>
    public static MailForCraftOrder ForProcessFee(uint receiverId, string receiverName, uint craftId, int copper)
    {
        var mail = ForReceiver(receiverId, receiverName, CraftOrderProcessRules.FeeMailSender, craftId);
        if (copper > 0)
            mail.AttachMoney(copper);
        return mail;
    }

    /// <summary>
    /// The requester's letter when the listing lapses or a GM wipe refunds it: the escrowed
    /// fee and the request sheet come back.
    /// </summary>
    public static MailForCraftOrder ForExpiredRefund(
        uint receiverId, string receiverName, uint craftId, int copper, Item sheet = null)
    {
        var mail = ForReceiver(receiverId, receiverName, CraftOrderProcessRules.ExpiredMailSender, craftId);
        if (copper > 0)
            mail.AttachMoney(copper);
        if (sheet != null)
        {
            sheet.OwnerId = receiverId;
            sheet.SlotType = SlotType.Mail;
            mail.Body.Attachments.Add(sheet);
        }

        return mail;
    }

    private static MailForCraftOrder ForReceiver(uint receiverId, string receiverName, string sender, uint craftId)
    {
        var mail = new MailForCraftOrder
        {
            ReceiverName = receiverName ?? string.Empty,
            Title = CraftOrderProcessRules.MailArgument(craftId)
        };
        mail.Header.SenderName = sender;
        mail.Header.ReceiverId = receiverId;
        mail.Body.Text = CraftOrderProcessRules.MailBodyArgument(craftId);
        return mail;
    }
}
