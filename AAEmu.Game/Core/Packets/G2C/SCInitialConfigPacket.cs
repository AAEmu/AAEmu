using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInitialConfigPacket : GamePacket
    {
        public SCInitialConfigPacket() : base(0x005, 2)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write("aa.mail.ru"); //host
            stream.Write(new byte[] {0x3E, 0x32, 0x0F, 0x0F, 0x79, 0x00, 0x33}, true); // fset
            stream.Write(0); // count
            stream.Write((byte) 0); // searchLevel
            stream.Write((byte) 10); // bidLevel
            stream.Write((byte) 0); // postLevel
            stream.Write(50); // initLp
            stream.Write(false); // canPlaceHouse
            stream.Write(false); // canPayTax
            stream.Write(true); // canUseAuction
            stream.Write(true); // canTrade
            stream.Write(true); // canSendMail
            stream.Write(true); // canUseBank
            stream.Write(true); // canUseCopper

            return stream;
        }
    }
}