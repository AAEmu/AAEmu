using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyLeavePacket : GamePacket
    {
        public CSFamilyLeavePacket() : base(0x01c, 1)  //TODO : 1.0 opcode: 0x01b
        {
        }

        public override void Read(PacketStream stream)
        {
            if (DbLoggerCategory.Database.Connection.ActiveChar.Family > 0)
                FamilyManager.Instance.LeaveFamily(DbLoggerCategory.Database.Connection.ActiveChar);
            _log.Debug("FamilyLeave");
        }
    }
}
