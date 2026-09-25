using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the client to drop its cached random shop record for one type.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value: s32 state · u32 type - a fixed eight-byte body. Semantics come from
/// the client's consumer: state 1 and state 7 clear the cached record for that type and raise the
/// shop-again UI event (state 1 additionally flagged); every other state value is ignored
/// client-side.
/// </remarks>
public class SCRandomShopInfoResetPacket(int state, int @type) : GamePacket(SCOffsets.SCRandomShopInfoResetPacket, 1)
{
    /// <summary>Reset flavour: 1 and 7 clear the client's record, anything else is a no-op there.</summary>
    public int State { get; } = state;

    /// <summary>Record key whose cached entry is dropped.</summary>
    public int Type { get; } = @type;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(State);
        stream.Write(Type);
        return stream;
    }
}
