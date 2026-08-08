using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails;

public class CountUnreadMail : PacketMarshaler
{
    public int Sent { get; set; }
    public int Received { get; protected set; }
    public int MiaReceived { get; protected set; }
    public int CommercialReceived { get; protected set; }

    // 10.0.2.13: the client uses the TOTAL counts (not the unread ones) to size each received mail-list tab
    // and to decide whether to pull the full list (CSListMail). Sending totals as 0 left every list drawing
    // 0 rows AND made the client believe it already had everything (total <= loaded), so it never requested
    // the list. Track the real per-category totals of received (landed) mail.
    public int TotalReceived { get; protected set; }
    public int TotalMiaReceived { get; protected set; }
    public int TotalCommercialReceived { get; protected set; }

    public override PacketStream Write(PacketStream stream)
    {
        // Mail count payloads contain the four total counts followed by the four unread counts.
        stream.Write(0);                       // total_sent (no server-tracked sent box)
        stream.Write(TotalReceived);           // total_received
        stream.Write(TotalMiaReceived);        // total_miaReceived
        stream.Write(TotalCommercialReceived); // total_commercialReceived
        stream.Write(Sent);                    // unread_sent
        stream.Write(Received);                // unread_received
        stream.Write(MiaReceived);             // unread_miaReceived
        stream.Write(CommercialReceived);      // unread_commercialReceived
        return stream;
    }

    public void ResetReceived()
    {
        Received = 0;
        MiaReceived = 0;
        CommercialReceived = 0;
    }

    public void ResetTotals()
    {
        TotalReceived = 0;
        TotalMiaReceived = 0;
        TotalCommercialReceived = 0;
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

    // Mirrors UpdateReceived but for the per-category TOTAL (read + unread) of received mail.
    public void UpdateTotal(MailType mailType, int amount)
    {
        if (mailType == MailType.Charged || mailType == MailType.Promotion)
        {
            TotalCommercialReceived += amount;
        }
        else
        if (mailType == MailType.MiaRecv)
        {
            TotalMiaReceived += amount;
        }
        else
        {
            TotalReceived += amount;
        }
    }
}
