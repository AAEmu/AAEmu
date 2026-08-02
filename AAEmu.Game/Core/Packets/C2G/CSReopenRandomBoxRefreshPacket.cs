using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO: the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSReopenRandomBoxRefreshPacket() : GamePacket(CSOffsets.CSReopenRandomBoxRefreshPacket, 1)
{
    public bool IsCharge { get; private set; }
    public long ItemId { get; private set; }
    public int TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        IsCharge = stream.ReadBoolean();
        ItemId = stream.ReadInt64();
        TypeValue = stream.ReadInt32();
    }
}
