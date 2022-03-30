using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionCancelProtectionPacket : GamePacket
    {
        public CSFactionCancelProtectionPacket() : base(CSOffsets.CSFactionCancelProtectionPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFactionCancelProtectionPacket");
        }
    }
}
