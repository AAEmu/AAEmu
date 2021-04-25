using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACWorldQueuePacket: LoginPacket
    {
        public ACWorldQueuePacket() : base(LCOffsets.ACWorldQueuePacket)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) 0); // diw -> world id
            stream.Write(false); // isPremium
            stream.Write((ushort) 0); // myTurn
            stream.Write((ushort) 0); // normalLength
            stream.Write((ushort) 0); // premiumLength
            return stream;
        }
    }
}
