using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// sbyte todayType, uint count
/// </remarks>
public class CSTodayAssignmentAcceptAllPacket() : GamePacket(CSOffsets.CSTodayAssignmentAcceptAllPacket, 1)
{
    public sbyte TodayType { get; private set; }
    public uint Count { get; private set; }

    public override void Read(PacketStream stream)
    {
        TodayType = stream.ReadSByte();
        Count = stream.ReadUInt32();
    }
}
