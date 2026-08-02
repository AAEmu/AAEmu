using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Request current monitor-NPC spawn list. Sniff body is empty/zeros; reply with empty list until Phase 2.
/// </summary>
public class CSRequestMonitorNpcsInfoPacket() : GamePacket(CSOffsets.CSRequestMonitorNpcsInfoPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Body unused in CN sniff (12 zero/pad bytes).
        Connection.ActiveChar?.SendPacket(new SCSpawnedMonitorNpcsPacket());
        Logger.Debug("CSRequestMonitorNpcsInfo: sent empty SCSpawnedMonitorNpcs");
    }
}
