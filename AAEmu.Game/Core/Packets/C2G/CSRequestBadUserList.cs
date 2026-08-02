using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte startIdx, sbyte endIdx
/// </remarks>
public class CSRequestBadUserList() : GamePacket(CSOffsets.CSRequestBadUserList, 1)
{
    public sbyte StartIdx { get; private set; }
    public sbyte EndIdx { get; private set; }

    public override void Read(PacketStream stream)
    {
        StartIdx = stream.ReadSByte();
        EndIdx = stream.ReadSByte();
    }
}
