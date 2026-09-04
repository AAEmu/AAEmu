using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Empty-body poll the client fires while the guild-war kill scoreboard is open (and periodically).
/// 2026-09-02: now answered with SCExpeditionWarKillScorePacket instead of being a no-op.
/// </summary>
public class CSExpeditionWarKillScorePacket() : GamePacket(CSOffsets.CSExpeditionWarKillScorePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        ExpeditionManager.Instance.SendWarKillScore(Connection);
    }
}
