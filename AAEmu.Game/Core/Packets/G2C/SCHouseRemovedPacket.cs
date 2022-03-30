using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseRemovedPacket : GamePacket
    {
        private readonly ushort _tl;
        
        public SCHouseRemovedPacket(ushort tl) : base(SCOffsets.SCHouseRemovedPacket, 5)
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
