using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCAbilitySetUpdatedPacket(sbyte unnamed1, sbyte responseType, uint slotIndex, sbyte usedFreeActivationCount) : GamePacket(SCOffsets.SCAbilitySetUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(unnamed1);
        stream.Write(responseType);
        stream.Write(slotIndex);
        stream.Write(usedFreeActivationCount);
        return stream;
    }
}
