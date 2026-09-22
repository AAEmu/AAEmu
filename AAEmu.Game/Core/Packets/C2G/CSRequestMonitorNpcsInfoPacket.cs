using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Request the monitor-NPC templates that currently have at least one live instance.
/// </summary>
public class CSRequestMonitorNpcsInfoPacket() : GamePacket(CSOffsets.CSRequestMonitorNpcsInfoPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        MonitorNpcGameData.Instance.PublishSnapshot(spawned =>
        {
            character.SendPacket(new SCSpawnedMonitorNpcsPacket(spawned));
            Logger.Debug("CSRequestMonitorNpcsInfo: sent {0} live template(s)", spawned.Length);
        });
    }
}
