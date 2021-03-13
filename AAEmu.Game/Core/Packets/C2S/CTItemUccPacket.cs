using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTItemUccPacket : StreamPacket
    {
        public CTItemUccPacket() : base(0x10)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt32();
            var num = stream.ReadUInt32();
            var itemId = new ulong[num];
            for (var i = 0; i < num; i++) // num, max 28
            {
                itemId[i] = stream.ReadUInt64();
            }
        }
    }
}
