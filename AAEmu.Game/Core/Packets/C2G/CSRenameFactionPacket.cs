using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRenameFactionPacket : GamePacket
    {
        public CSRenameFactionPacket() : base(CSOffsets.CSRenameFactionPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRenameFactionPacket");
        }
    }
}
