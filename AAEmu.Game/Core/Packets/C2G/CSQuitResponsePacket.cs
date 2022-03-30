using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSQuitResponsePacket : GamePacket
    {
        public CSQuitResponsePacket() : base(CSOffsets.CSQuitResponsePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSQuitResponsePacket");
        }
    }
}
