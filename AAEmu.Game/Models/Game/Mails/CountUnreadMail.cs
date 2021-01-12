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
    }
}
