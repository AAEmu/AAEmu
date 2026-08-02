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
public class CSRaidApplicantAcceptReplyPacket() : GamePacket(CSOffsets.CSRaidApplicantAcceptReplyPacket, 1)
{
    public ulong TypeValue { get; private set; }
    public bool Join { get; private set; }
    public uint Role { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();
        Join = stream.ReadBoolean();
        Role = stream.ReadUInt32();
    }
}
