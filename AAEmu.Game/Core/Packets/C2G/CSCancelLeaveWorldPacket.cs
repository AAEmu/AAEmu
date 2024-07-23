using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCancelLeaveWorldPacket : GamePacket
{
    public CSCancelLeaveWorldPacket() : base(CSOffsets.CSCancelLeaveWorldPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        if (Connection?.LeaveTask == null)
            return;
        // if (Connection.LeaveTask.Status != TaskStatus.Running)
        //     return;
        Connection.CancelTokenSource.Cancel();
        Connection.CancelTokenSource.Dispose();
        Connection.LeaveTask = null;
        Connection.SendPacket(new SCLeaveWorldCanceledPacket());
    }
}
