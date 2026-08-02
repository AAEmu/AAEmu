using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Models.Tasks.Network;

/// <summary>
/// Server-initiated game-channel keepalive. Mirrors the real server's periodic ping scheduler
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
            // Environment.TickCount64 (process uptime, not the game physics clock), corrupting the context time base
            // so world objects (including the local player unit) never bind — leaving *(player+368) null.
            //
            // But in-world we ALSO can't go silent: with zone authority the native zone owns NPC movement and we
            // relay ~0 SCUnitMovements, so after the enter-spawn burst drains there is no periodic server→client
            // traffic. The client's recv watchdog then trips a few seconds into an idle world and it cleanly
            // quits (Save Game Option → System:Quit, no leave handshake). Commercial never goes silent (constant
            // SCUnitMovements). So in-world send a cheap, tPhy-free keepalive instead of a ping: re-emit the
            // player's own HP/MP points. The client applies identical values (no visible change) but the packet
            // keeps the recv watchdog fed until real NPC movement relay lands.
            if (connection.State == GameState.World)
            {
                var character = connection.ActiveChar;
                if (character != null)
                    connection.SendPacket(new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp));
                continue;
            }

            connection.SendPacket(new PingPacket());
        }
    }
}
