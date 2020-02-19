using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mails
{
    public class CountUnreadMail : PacketMarshaler
    {
        private int Sent { get; set; }
        private int Received { get; set; }
        private int MiaReceived { get; set; }
        private int CommercialReceived { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Sent);
            stream.Write(Received);
            stream.Write(MiaReceived);
            stream.Write(CommercialReceived);
            return stream;
        }
    }
}
