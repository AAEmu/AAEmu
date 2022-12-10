using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails
{
    public class CountUnreadMail : PacketMarshaler
    {
        public int TotalSent { get; set; }
        public int TotalReceived { get; set; }
        public int TotalMiaReceived { get; set; }
        public int TotalCommercialReceived { get; set; }
        public int UnreadSent { get; set; }
        public int UnreadReceived { get; set; }
        public int UnreadMiaReceived { get; set; }
        public int UnreadCommercialReceived { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(TotalSent);
            stream.Write(TotalReceived);
            stream.Write(TotalMiaReceived);
            stream.Write(TotalCommercialReceived);
            stream.Write(UnreadSent);
            stream.Write(UnreadReceived);
            stream.Write(UnreadMiaReceived);
            stream.Write(UnreadCommercialReceived);

            return stream;
        }

        public void ResetReceived()
        {
            TotalReceived = 0;
            TotalMiaReceived = 0;
            TotalCommercialReceived = 0;
            UnreadReceived = 0;
            UnreadMiaReceived = 0;
            UnreadCommercialReceived = 0;
        }

        public void UpdateReceived(MailType mailType, int amount)
        {
            if (mailType == MailType.Charged || mailType == MailType.Promotion)
            {
                TotalCommercialReceived += amount;
                UnreadCommercialReceived += amount;
            }
            else
            if (mailType == MailType.MiaRecv)
            {
                TotalMiaReceived += amount;
                UnreadMiaReceived += amount;
            }
            else
            {
                TotalReceived += amount;
                UnreadReceived += amount;
            }
        }

    }
}
