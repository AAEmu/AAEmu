using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSNotifyInGameCompletedPacket : GamePacket
{
    public CSNotifyInGameCompletedPacket() : base(CSOffsets.CSNotifyInGameCompletedPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Connection.SendPacket(new SCScheduledEventStartedPacket());
        Connection.SendPacket(new SCGlobalGameStatusAckPacket());
        Connection.SendPacket(new SCSpawnedMonitorNpcsPacket());

        Connection.SendPacket(new SCChatMessagePacket(ChatType.System, AppConfiguration.Instance.World.MOTD)); // "Welcome to AAEmu!"
        Connection.SendPacket(new SCDelayedTaskOnInGameNotifyPacket());

        WorldManager.Instance.OnPlayerJoin(Connection.ActiveChar);
        Logger.Info("NotifyInGameCompleted SubZoneId {0}", Connection.ActiveChar.SubZoneId);
    }
}
