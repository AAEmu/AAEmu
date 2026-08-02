using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Empty body in CN sniff is count=0 → two zero bytes.
/// </summary>
public class SCSpawnedMonitorNpcsPacket(uint[] types = null) : GamePacket(SCOffsets.SCSpawnedMonitorNpcsPacket, 1)
{
    private readonly uint[] _types = types ?? [];

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)_types.Length);
        foreach (var type in _types)
            stream.Write(type);
        return stream;
    }
}
