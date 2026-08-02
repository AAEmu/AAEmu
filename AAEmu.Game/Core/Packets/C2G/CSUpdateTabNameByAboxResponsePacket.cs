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
public class CSUpdateTabNameByAboxResponsePacket() : GamePacket(CSOffsets.CSUpdateTabNameByAboxResponsePacket, 1)
{
    public ulong WebSessionKey { get; private set; }
    public uint QueryNo { get; private set; }
    public bool Result { get; private set; }

    public override void Read(PacketStream stream)
    {
        WebSessionKey = stream.ReadUInt64();
        QueryNo = stream.ReadUInt32();
        Result = stream.ReadBoolean();
    }
}
