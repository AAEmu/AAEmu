using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails;

public class MailBody(BaseMail parent) : PacketMarshaler
{
    public const byte MaxMailAttachments = 10;
    private string _text;
    private int _copperCoins;
    private int _billingAmount;
    private int _moneyAmount2;
    private DateTime _sendDate;
    private DateTime _recvDate;

    public long MailId { get => parent.Id; }
    public MailType Type { get => parent.MailType; }
    public string ReceiverName { get => parent.ReceiverName; }
    public string Title { get => parent.Title; }
    public string Text { get => _text; set { _text = value; parent.IsDirty = true; } }
    public int CopperCoins { get => _copperCoins; set { _copperCoins = value; parent.IsDirty = true; } }
    public int BillingAmount { get => _billingAmount; set { _billingAmount = value; parent.IsDirty = true; } }
    public int MoneyAmount2 { get => _moneyAmount2; set { _moneyAmount2 = value; parent.IsDirty = true; } }
    public DateTime SendDate { get => _sendDate; set { _sendDate = value; parent.IsDirty = true; } }
    public DateTime RecvDate { get => _recvDate; set { _recvDate = value; parent.IsDirty = true; } }
    public DateTime OpenDate { get => parent.OpenDate; }
    public List<Item> Attachments { get; set; } = []; // TODO max length 10

    public override PacketStream Write(PacketStream stream)
    {
        // Monetary fields use their full storage widths so attachments stay aligned.
        stream.Write(MailId);
        stream.Write((byte)Type);
        stream.Write(ReceiverName);
        stream.Write(Title);
        stream.Write(Text);
        stream.Write((long)CopperCoins);
        stream.Write((long)BillingAmount);
        stream.Write((long)MoneyAmount2);
        stream.Write(0u);
        stream.Write(SendDate);
        stream.Write(RecvDate);
        stream.Write(OpenDate);
        for (var i = 0; i < MaxMailAttachments; i++)
        {
            if (i >= Attachments.Count || Attachments[i] == null)
                stream.Write(0); // templateId empty → client skips item body
            else
                stream.Write(Attachments[i]);
        }

        return stream;
    }
}
