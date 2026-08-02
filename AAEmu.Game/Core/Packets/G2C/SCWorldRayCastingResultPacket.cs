using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The native receive buffer caps text at 0x1ff bytes.
/// </summary>
public class SCWorldRayCastingResultPacket(
    uint id,
    ulong x,
    ulong y,
    float z,
    string text) : GamePacket(SCOffsets.SCWorldRayCastingResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        stream.Write(text ?? string.Empty);
        return stream;
    }
}
