using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListMailPacket : GamePacket
    {
        public CSListMailPacket() : base(0x099, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            Connection.ActiveChar.Mails.Send();
        }
    }
}
