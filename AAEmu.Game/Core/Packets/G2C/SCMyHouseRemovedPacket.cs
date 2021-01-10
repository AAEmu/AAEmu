using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMyHouseRemovedPacket : GamePacket
    {
        private readonly ushort _tl;
        
        public SCMyHouseRemovedPacket(ushort tl) : base(SCOffsets.SCMyHouseRemovedPacket, 5)
        {
            _tl = tl;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            return stream;
        }
    }
}
