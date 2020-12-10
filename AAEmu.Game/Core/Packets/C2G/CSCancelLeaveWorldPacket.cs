using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelLeaveWorldPacket : GamePacket
    {
        public CSCancelLeaveWorldPacket() : base(CSOffsets.CSCancelLeaveWorldPacket, 1)
        {
        }

        public override async void Read(PacketStream stream)
        {
            if (Connection?.LeaveTask == null)
                return;

            var result = await Connection.LeaveTask.Cancel();
            if (result)
            {
                Connection.LeaveTask = null;
                Connection.SendPacket(new SCLeaveWorldCanceledPacket());
            }
        }
    }
}
