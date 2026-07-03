using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Models.Tasks.Network;

/// <summary>
/// Server-initiated game-channel keepalive. Mirrors the reference server's periodic ping scheduler,
/// which constructs and sends X2::PingPacket at level 2 on a timer.
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

            // 10.0.2.13 does NOT server-initiate pings once the client is entering the world: a live capture shows
            // the server only PONGs (subtype 19) the client's pings (subtype 18). A server-initiated ping carries
            // tPhy, which the client's CryNetwork context view uses as its physics-time reference; our tPhy is
            // Environment.TickCount64 (process uptime, not the game physics clock), corrupting the context time base
            // so world objects (including the local player unit) never bind — leaving *(player+368) null. From
            // SpawnCharacter onward (State == World) the client's own pings keep the channel alive, so stop the
            // server-initiated ping there; it stays on only for the lobby/char-list keepalive.
            if (connection.State == GameState.World)
                continue;

            connection.SendPacket(new PingPacket());
        }
    }
}
