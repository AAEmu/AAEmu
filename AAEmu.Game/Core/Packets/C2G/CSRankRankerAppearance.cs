using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte worldid, ulong type
/// </remarks>
public class CSRankRankerAppearance() : GamePacket(CSOffsets.CSRankRankerAppearance, 1)
{
    public sbyte Worldid { get; private set; }
    public ulong Type { get; private set; }

    public override void Read(PacketStream stream)
    {
        Worldid = stream.ReadSByte();
        Type = stream.ReadUInt64();
    }
}
