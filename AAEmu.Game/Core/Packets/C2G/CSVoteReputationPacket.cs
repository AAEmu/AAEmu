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
public class CSVoteReputationPacket() : GamePacket(CSOffsets.CSVoteReputationPacket, 1)
{
    public ulong TypeValue { get; private set; }
    public uint Add { get; private set; }
    public bool ByGm { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();
        Add = stream.ReadUInt32();
        ByGm = stream.ReadBoolean();
    }
}
