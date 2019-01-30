using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTQueryCharNamePacket : StreamPacket
    {
        public CTQueryCharNamePacket() : base(0x0a)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt32();
        }
    }
}