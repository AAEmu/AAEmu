using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSpecialtyRatioPacket : GamePacket
    {
        private readonly int _ratio;

        public SCSpecialtyRatioPacket(int ratio) : base(SCOffsets.SCSpecialtyRatioPacket, 5)
        {
            _ratio = ratio;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_ratio);
            return stream;
        }
    }
}
