using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// The letter that carries a reopen-box roll once the box's life time has passed.
/// The sender key is the mailbox locale entry the client already ships for this letter.
/// </summary>
public sealed class MailForReopenBox : BaseMail
{
    public const string SenderKey = ".reopenRandomBox";

    private MailForReopenBox()
    {
        MailType = MailType.SysExpress;
        Header.SenderId = 0;
        Header.Status = MailStatus.Unread;
        var now = DateTime.UtcNow;
        Body.SendDate = now;
        Body.RecvDate = now;
    }

    public static MailForReopenBox ForExpiredRoll(
        uint receiverId, string receiverName, uint packId, uint goodId, Item reward)
    {
        var mail = new MailForReopenBox
        {
            ReceiverName = receiverName ?? string.Empty,
            Title = $"title({packId})",
        };
        mail.Header.SenderName = SenderKey;
        mail.Header.ReceiverId = receiverId;
        mail.Body.Text = $"body({goodId})";
        if (reward != null)
        {
            reward.OwnerId = receiverId;
            reward.SlotType = SlotType.Mail;
            mail.Body.Attachments.Add(reward);
        }

        return mail;
    }
}
