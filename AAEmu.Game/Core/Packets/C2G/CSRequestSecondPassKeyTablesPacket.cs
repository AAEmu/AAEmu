using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestSecondPassKeyTablesPacket : GamePacket
    {
        public CSRequestSecondPassKeyTablesPacket() : base(CSOffsets.CSRequestSecondPassKeyTablesPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestSecondPassKeyTablesPacket");
        }
    }
}
