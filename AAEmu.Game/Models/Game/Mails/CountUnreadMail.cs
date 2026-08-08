using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>Tracks total and unread counts for each mailbox category.</summary>
public class CountUnreadMail : PacketMarshaler
{
    public int TotalSent { get; set; }
    public int TotalReceived { get; set; }
    public int TotalMiaReceived { get; set; }
    public int TotalCommercialReceived { get; set; }

    public int Sent { get; set; }
    public int Received { get; protected set; }
    public int MiaReceived { get; protected set; }
    public int CommercialReceived { get; protected set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(TotalSent);
        stream.Write(TotalReceived);
        stream.Write(TotalMiaReceived);
        stream.Write(TotalCommercialReceived);
        stream.Write(Sent);
        stream.Write(Received);
        stream.Write(MiaReceived);
        stream.Write(CommercialReceived);
        return stream;
    }

    public void ResetReceived()
    {
        Received = 0;
        MiaReceived = 0;
        CommercialReceived = 0;
    }

    public void ResetAll()
    {
        TotalSent = 0;
        TotalReceived = 0;
        TotalMiaReceived = 0;
        TotalCommercialReceived = 0;
        Sent = 0;
        ResetReceived();
    }

    public void UpdateReceived(MailType mailType, int amount)
    {
        if (mailType is MailType.Charged or MailType.Promotion)
            CommercialReceived += amount;
        else if (mailType == MailType.MiaRecv)
            MiaReceived += amount;
        else
            Received += amount;
    }

    public void AddTotal(MailType mailType, int amount = 1)
    {
        if (mailType is MailType.Charged or MailType.Promotion)
            TotalCommercialReceived += amount;
        else if (mailType == MailType.MiaRecv)
            TotalMiaReceived += amount;
        else
            TotalReceived += amount;
    }
}
