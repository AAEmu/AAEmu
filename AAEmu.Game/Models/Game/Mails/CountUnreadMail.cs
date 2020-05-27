using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;

namespace AAEmu.Game.Models.Game.Mails
{
    public class CountUnreadMail : PacketMarshaler
    {
        public int Sent { get; set; }
        public int Received { get; set; }
        public int MiaReceived { get; set; }
        public int CommercialReceived { get; set; }

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
