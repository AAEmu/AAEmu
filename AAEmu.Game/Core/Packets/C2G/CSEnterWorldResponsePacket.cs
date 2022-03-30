using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEnterWorldResponsePacket : GamePacket
    {
        public CSEnterWorldResponsePacket() : base(CSOffsets.SCEnterWorldResponsePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSEnterWorldResponsePacket");
        }
    }
}
