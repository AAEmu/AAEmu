using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTodayAssignmentPacket : GamePacket
    {
        public CSTodayAssignmentPacket() : base(CSOffsets.CSTodayAssignmentPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSTodayAssignmentPacket");
        }
    }
}
