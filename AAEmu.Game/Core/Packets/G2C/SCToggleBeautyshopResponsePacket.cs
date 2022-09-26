using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCToggleBeautyShopResponsePacket : GamePacket
    {
        private readonly byte _state;

        public SCToggleBeautyShopResponsePacket(byte state) : base(SCOffsets.SCToggleBeautyShopResponsePacket, 5)
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
