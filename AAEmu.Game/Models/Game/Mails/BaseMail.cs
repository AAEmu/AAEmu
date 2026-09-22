using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails;

public class BaseMail
{
    private long _id;
    private MailType _mailType;
    private string _title;
    private string _receiverName;
    private readonly LiveDirtyGate _dirty = new();
    private MailHeader _header;
    private MailBody _body;
    private DateTime _openDate;

    public long Id { get => _id; set { _id = value; MarkDirty(); } }
    public MailType MailType { get => _mailType; set { _mailType = value; MarkDirty(); } }
    public string Title { get => _title; set { _title = value; MarkDirty(); } }
    public string ReceiverName { get => _receiverName; set { _receiverName = value; MarkDirty(); } }
    public DateTime OpenDate { get => _openDate; set { _openDate = value; MarkDirty(); } }

    public MailHeader Header { get => _header; set { _header = value; MarkDirty(); } }
    public MailBody Body { get => _body; set { _body = value; MarkDirty(); } }

    // Local helpers
    public bool IsDelivered { get; set; }

    // Retention: per-side logical deletion. The row is removed physically only when both sides are gone.
    private bool _senderDeleted;
    private bool _receiverDeleted;

    public bool SenderDeleted { get => _senderDeleted; set { _senderDeleted = value; MarkDirty(); } }
    public bool ReceiverDeleted { get => _receiverDeleted; set { _receiverDeleted = value; MarkDirty(); } }
    public int DirtyStamp => _dirty.Stamp;

    public bool IsDirty
    {
        get => _dirty.IsDirty;
        set => _dirty.IsDirty = value;
    }

    public bool TryCaptureDirtyStamp(out int stamp) => _dirty.TryCapture(out stamp);

    public bool TryClearDirty(int writtenStamp) => _dirty.TryClear(writtenStamp);

    private void MarkDirty() => _dirty.Mark();

    /// <summary>
    /// Staged on a caller transaction that has not committed. Mailbox list, claim, and
    /// the world save must ignore it until <see cref="MailManager.PublishDelivered"/>.
    /// </summary>
    public bool IsPendingPublish { get; set; }

    public BaseMail()
    {
        Header = new MailHeader(this);
        Body = new MailBody(this);
        IsDelivered = false;
    }

    public bool Send()
    {
        MailDeliveryRules.PrepareAttachments(this);
        return MailManager.Instance.Send(this);
    }

    internal void PrepareForSend()
    {
        Header.Attachments = GetTotalAttachmentCount();
        RenumberSlots();
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
               && MailType is MailType.Normal or MailType.Express or MailType.Spam;
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

    protected void RenumberSlots() => MailDeliveryRules.PrepareAttachments(this);
}
