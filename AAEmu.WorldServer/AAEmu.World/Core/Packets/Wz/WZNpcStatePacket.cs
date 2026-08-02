using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZNpcState (0x002) — full NPC snapshot so Zone runs NpcManager::Create + CreateAI.
/// Body built by <see cref="AAEmu.Game.WorldIntegration.BuildWzNpcStateBody"/>.
/// </summary>
public class WZNpcStatePacket(byte[] body) : ZonePacket(WzOpcodes.NpcState)
{
    protected override void WriteBody(PacketStream stream)
    {
        if (body is { Length: > 0 })
            stream.Write(body, false);
    }
}
