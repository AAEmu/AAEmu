using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelLeaveWorldPacket : GamePacket
    {
        public CSCancelLeaveWorldPacket() : base(0x002, 1)
        {
        }

        public override async void Read(PacketStream stream)
        {
            if (Connection?.LeaveTask == null)
                return;

            var result = await DbLoggerCategory.Database.Connection.LeaveTask.Cancel();
            if (result)
            {
                DbLoggerCategory.Database.Connection.LeaveTask = null;
                DbLoggerCategory.Database.Connection.SendPacket(new SCLeaveWorldCanceledPacket());
            }
        }
    }
}