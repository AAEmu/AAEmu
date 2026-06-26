using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Models.Tasks.Network;

/// <summary>
/// Server-initiated game-channel keepalive. Mirrors the real server's periodic ping scheduler
/// (crynetwork_dedicate.dll sub_3957FCC0, which constructs+sends X2::PingPacket at level 2 on a timer).
/// Without periodic server→client traffic the 10.0.2.13 client's recv watchdog times out
/// (recv exception internal 4 wsa 258) ~2s after the char-list and drops the game connection.
/// </summary>
public class GamePingTask : Task
{
    public override void Execute()
    {
        foreach (var connection in GameConnectionTable.Instance.GetConnections())
        {
            // Only ping channels whose enter-world handshake is complete (X2EnterWorldResponse sent);
            // before that the enter-world packet burst already keeps the channel busy.
            if (!connection.EncryptionActive)
                continue;

            connection.SendPacket(new PingPacket());
        }
    }
}
