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
public class CSInstantTimePacket() : GamePacket(CSOffsets.CSInstantTimePacket, 1)
{
    public uint TimeType { get; private set; }
    public bool Use { get; private set; }
    public bool SaveDb { get; private set; }

    public override void Read(PacketStream stream)
    {
        TimeType = stream.ReadUInt32();
        Use = stream.ReadBoolean();
        SaveDb = stream.ReadBoolean();
    }
}
