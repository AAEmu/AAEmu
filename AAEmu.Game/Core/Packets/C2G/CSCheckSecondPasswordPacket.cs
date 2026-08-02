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
public class CSCheckSecondPasswordPacket() : GamePacket(CSOffsets.CSCheckSecondPasswordPacket, 1)
{
    public int Time { get; private set; }
    public sbyte TableIndex { get; private set; }
    public string Pass { get; private set; }

    public override void Read(PacketStream stream)
    {
        Time = stream.ReadInt32();
        TableIndex = stream.ReadSByte();
        Pass = stream.ReadString();
    }
}
