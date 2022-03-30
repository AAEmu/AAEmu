using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSThisTimeUnpackPacket : GamePacket
    {
        public CSThisTimeUnpackPacket() : base(CSOffsets.CSThisTimeUnpackPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSThisTimeUnpackPacket");
        }
    }
}
