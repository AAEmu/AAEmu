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
public class CSChangeSecondPasswordPacket() : GamePacket(CSOffsets.CSChangeSecondPasswordPacket, 1)
{
    public int Time { get; private set; }
    public sbyte OldPassTableIndex { get; private set; }
    public sbyte NewPassTableIndex { get; private set; }
    public string OldPass { get; private set; }

    public override void Read(PacketStream stream)
    {
        Time = stream.ReadInt32();
        OldPassTableIndex = stream.ReadSByte();
        NewPassTableIndex = stream.ReadSByte();
        OldPass = stream.ReadString();
    }
}
