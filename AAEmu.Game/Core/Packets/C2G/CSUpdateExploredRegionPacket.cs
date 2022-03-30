using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUpdateExploredRegionPacket : GamePacket
    {
        public CSUpdateExploredRegionPacket() : base(CSOffsets.CSUpdateExploredRegionPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSUpdateExploredRegionPacket");
        }
    }
}
