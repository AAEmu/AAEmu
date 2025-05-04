using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNotifyInGameCompletedPacket() : GamePacket(CSOffsets.CSNotifyInGameCompletedPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        WorldManager.Instance.OnPlayerJoin(Connection.ActiveChar);
        Logger.Info($"NotifyInGameCompleted SubZoneId {Connection.ActiveChar.SubZoneId}, {Connection.ActiveChar?.Name} ({Connection.ActiveChar?.Id})");
    }
}
