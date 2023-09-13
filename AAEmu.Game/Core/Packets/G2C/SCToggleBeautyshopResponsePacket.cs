using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCToggleBeautyshopResponsePacket : GamePacket
    {
        private readonly byte _state;

        public SCToggleBeautyshopResponsePacket(byte state) : base(SCOffsets.SCToggleBeautyshopResponsePacket, 1)
        {
            _state = state;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_state);
            return stream;
        }
    }
}
