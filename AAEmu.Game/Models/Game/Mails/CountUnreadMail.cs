using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails;

public class CountUnreadMail : PacketMarshaler
{
    public int Sent { get; set; }
    public int Received { get; protected set; }
    public int MiaReceived { get; protected set; }
    public int CommercialReceived { get; protected set; }

    public override PacketStream Write(PacketStream stream)
    {
        // the mail body/list packets): 8 u32 — the four total_* counts then the four unread_* counts. AAEmu
        // tracks only the unread counts; totals go out as 0, matching the mail block in SCCharacterState.
        stream.Write(0);                  // total_sent
        stream.Write(0);                  // total_received
        stream.Write(0);                  // total_miaReceived
        stream.Write(0);                  // total_commercialReceived
        stream.Write(Sent);               // unread_sent
        stream.Write(Received);           // unread_received
        stream.Write(MiaReceived);        // unread_miaReceived
        stream.Write(CommercialReceived); // unread_commercialReceived
        return stream;
    }

    public void ResetReceived()
    {
        Received = 0;
        MiaReceived = 0;
        CommercialReceived = 0;
    }

    public void UpdateReceived(MailType mailType, int amount)
    {
        if (mailType == MailType.Charged || mailType == MailType.Promotion)
        {
            CommercialReceived += amount;
        }
        else
        if (mailType == MailType.MiaRecv)
        {
            MiaReceived += amount;
        }
        else
        {
            Received += amount;
        }
    }
}
