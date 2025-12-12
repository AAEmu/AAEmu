using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLeaveInstantGamePacket() : GamePacket(CSOffsets.CSLeaveInstantGamePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty packet - no data to read
        Logger.Warn("LeaveInstantGame");

        Connection.ActiveChar.CurrentInstantGame?.LeaveInstantGame(Connection.ActiveChar);
    }
}