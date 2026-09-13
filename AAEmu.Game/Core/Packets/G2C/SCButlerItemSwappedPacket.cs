using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Completes the client's optimistic farmhand item reservation. The four location bytes are the
/// request tuple, followed by the durable farmhand item id and a raw ErrorMessage value.
/// </summary>
/// <remarks>
/// Opcode and 14-byte body are from 10.0.2.13 FUN_39C76300/FUN_39CDDD60. The success handler is a
/// no-op; failures use this echoed tuple to release both reserved locations.
/// </remarks>
public sealed class SCButlerItemSwappedPacket(
    byte bagType,
    byte bagIndex,
    byte butlerType,
    byte butlerIndex,
    ulong butlerItemId,
    ushort errorMessage) : GamePacket(SCOffsets.SCButlerItemSwappedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(bagType);
        stream.Write(bagIndex);
        stream.Write(butlerType);
        stream.Write(butlerIndex);
        stream.Write(butlerItemId);
        stream.Write(errorMessage);
        return stream;
    }
}
