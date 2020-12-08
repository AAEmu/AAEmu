using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyLeavePacket : GamePacket
    {
        public CSFamilyLeavePacket() : base(CSOffsets.CSFamilyLeavePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            if (Connection.ActiveChar.Family > 0)
                FamilyManager.Instance.LeaveFamily(Connection.ActiveChar);
            _log.Debug("FamilyLeave");
        }
    }
}
