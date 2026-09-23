using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answer to <see cref="C2G.CSChronicleInfoBuyPacket"/>: whether the saga group's chronicle info
/// was created, and the status the group now holds.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: u8 result, u16 ErrorMessage, u32 type, u8 status.
/// </remarks>
public class SCChronicleInfoBuyPacket(bool result, ErrorMessageType errorMessage, int type, sbyte status)
    : GamePacket(SCOffsets.SCChronicleInfoBuyPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write((ushort)errorMessage);
        stream.Write(type);
        stream.Write(status);
        return stream;
    }
}
