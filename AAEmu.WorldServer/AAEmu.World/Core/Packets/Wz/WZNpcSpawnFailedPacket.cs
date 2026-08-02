using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// </summary>
public sealed class WZNpcSpawnFailedPacket(byte[] spawnNpcBody) : ZonePacket(WzOpcodes.NpcSpawnFailed)
{
    protected override void WriteBody(PacketStream stream)
    {
        if (spawnNpcBody is { Length: > 0 })
            stream.Write(spawnNpcBody, false);
    }
}
