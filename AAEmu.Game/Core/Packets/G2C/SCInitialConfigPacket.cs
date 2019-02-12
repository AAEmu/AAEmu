using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInitialConfigPacket : GamePacket
    {
        public SCInitialConfigPacket() : base(0x032, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write("aaemu.local"); // host
            stream.Write(new byte[] {0x3E, 0x32, 0x0F, 0x0F, 0x79, 0x08, 0x33, 0x00, 0x00, 0x00, 0x03}, true); // fset
            // TODO 0x3E, 0x32, 0x0F, 0x0F, 0x79, 0x00, 0x33
            // TODO 0x7F, 0x37, 0x34, 0x0F, 0x79, 0x08, 0x7D, 0xCB, 0x37, 0x65, 0x03, 0xDE, 0xAE, 0x86, 0x3C, 0x0E, 0x02, 0xE6, 0x6F, 0xC7, 0xBB, 0x9B, 0x5D, 0x01, 0x00, 0x01

            stream.Write(0); // count

            stream.Write((byte)0); // searchLevel
            stream.Write((byte)10); // bidLevel
            stream.Write((byte)0); // postLevel

            stream.Write(50); // initLp
            stream.Write(false); // canPlaceHouse
            stream.Write(false); // canPayTax
            stream.Write(true); // canUseAuction
            stream.Write(true); // canTrade
            stream.Write(true); // canSendMail
            stream.Write(true); // canUseBank
            stream.Write(true); // canUseCopper

            stream.Write((byte)0); // secondPriceType
            stream.Write((byte)0); // secondPasswordMaxFailCount

            return stream;
        }
    }
}
