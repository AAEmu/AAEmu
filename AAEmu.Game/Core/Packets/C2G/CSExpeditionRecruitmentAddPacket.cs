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
public class CSExpeditionRecruitmentAddPacket() : GamePacket(CSOffsets.CSExpeditionRecruitmentAddPacket, 1)
{
    public short Interest { get; private set; }
    public uint Day { get; private set; }
    public string Introduce { get; private set; }

    public override void Read(PacketStream stream)
    {
        Interest = stream.ReadInt16();
        Day = stream.ReadUInt32();
        Introduce = stream.ReadString();
    }
}
