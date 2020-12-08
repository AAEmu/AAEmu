using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestSecondPasswordKeyTablesPacket :GamePacket
    {
        public CSRequestSecondPasswordKeyTablesPacket() : base(CSOffsets.CSRequestSecondPasswordKeyTablesPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("RequestSecondPasskeytable");
        }
    }
}

