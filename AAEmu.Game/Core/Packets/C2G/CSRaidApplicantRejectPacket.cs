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
public class CSRaidApplicantRejectPacket() : GamePacket(CSOffsets.CSRaidApplicantRejectPacket, 1)
{
    public uint Count { get; private set; }
    public ulong TypeValue { get; private set; }
    public ulong TypeValue2 { get; private set; }

    public override void Read(PacketStream stream)
    {
        Count = stream.ReadUInt32();
        TypeValue = stream.ReadUInt64();
        TypeValue2 = stream.ReadUInt64();
    }
}
