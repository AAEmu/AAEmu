using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails;

public class BaseMail
{
    private long _id;
    private MailType _mailType;
    private string _title;
    private string _receiverName;
    private bool _isDirty;
    private MailHeader _header;
    private MailBody _body;
    private DateTime _openDate;

    public long Id { get => _id; set { _id = value; _isDirty = true; } }
    public MailType MailType { get => _mailType; set { _mailType = value; _isDirty = true; } }
    public string Title { get => _title; set { _title = value; _isDirty = true; } }
    public string ReceiverName { get => _receiverName; set { _receiverName = value; _isDirty = true; } }
    public DateTime OpenDate { get => _openDate; set { _openDate = value; _isDirty = true; } }

    public MailHeader Header { get => _header; set { _header = value; _isDirty = true; } }
    public MailBody Body { get => _body; set { _body = value; _isDirty = true; } }

    // Local helpers
    public bool IsDelivered { get; set; }
    public bool IsDirty { get => _isDirty; set => _isDirty = value; }

    public BaseMail()
    {
        Header = new MailHeader(this);
        Body = new MailBody(this);
        IsDelivered = false;
    }

    public bool Send()
    {
        // Update Attachments just in case somebody did manual editing
        Header.Attachments = GetTotalAttachmentCount();
        RenumberSlots();
        return MailManager.Instance.Send(this);
    }

    /// <summary>
    /// Checks if a mail can returned to it's sender
    /// </summary>
    /// <returns></returns>
    public bool CanReturnMail()
    {
        return IsDelivered == false && Header.SenderId != Header.ReceiverId && Header.SenderId > 0 && (MailType == MailType.Normal || MailType == MailType.Express);
    }

    /// <summary>
    /// Whether <paramref name="characterId"/> may hand this mail back from their inbox.
    ///
    /// Deliberately not <see cref="CanReturnMail"/>. That test gates the character-deletion sweep on mail
    /// that never reached its recipient, so it requires <c>IsDelivered == false</c> — but delivery is set on
    /// notify, and on load for anything whose RecvDate has passed, meaning every mail a player can actually
    /// see in their inbox is already delivered. Reusing it would reject every return the client can ask for.
    /// </summary>
    public bool CanBeReturnedBy(uint characterId)
    {
        return Header.ReceiverId == characterId
               && Header.SenderId > 0
               && Header.SenderId != Header.ReceiverId
               && !Header.Returned
               && (MailType == MailType.Normal || MailType == MailType.Express);
    }

    /// <summary>
    /// Player-initiated return of a mail sitting in <paramref name="characterId"/>'s inbox.
    /// </summary>
    public bool ReturnToSenderFor(uint characterId)
    {
        return MailManager.Instance.TryReturnToSenderFor(this, characterId);
    }

    public bool ReturnToSender()
    {
        return MailManager.Instance.TryReturnToSender(this);
    }

    public byte GetTotalAttachmentCount()
    {
        var res = (byte)Body.Attachments.Count;
        if (Body.CopperCoins != 0)
            res++;
        if (Body.BillingAmount != 0)
            res++;
        if (Body.MoneyAmount2 != 0)
            res++;
        return res;
    }

    /// <summary>
    /// Adds money values to the body, does not actually reduce it from the player at this point
    /// </summary>
    /// <param name="copperCoinsAmount"></param>
    /// <param name="money1Amount"></param>
    /// <param name="money2Amount"></param>
    public void AttachMoney(int copperCoinsAmount, int money1Amount = 0, int money2Amount = 0)
    {
        Body.CopperCoins = copperCoinsAmount;
        Body.BillingAmount = money1Amount;
        Body.MoneyAmount2 = money2Amount;
        Header.Attachments = GetTotalAttachmentCount();
    }

    protected void RenumberSlots()
    {
        for (var i = 0; i < Body.Attachments.Count; i++)
        {
            Body.Attachments[i].SlotType = SlotType.Mail;
            Body.Attachments[i].Slot = i;
        }
    }
}
