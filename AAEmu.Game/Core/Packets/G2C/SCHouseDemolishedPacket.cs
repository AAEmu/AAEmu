using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseDemolishedPacket : GamePacket
    {
        private readonly ushort _tl;
        
        public SCHouseDemolishedPacket(ushort tl) : base(SCOffsets.SCHouseDemolishedPacket, 5)
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
