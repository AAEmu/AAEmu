using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// which passes each field name alongside the value:
/// int squadId, int inviationId, long worldCharKey, sbyte refuseType
/// </remarks>
public class CSRefuseSquadInvitation() : GamePacket(CSOffsets.CSRefuseSquadInvitation, 1)
{
    public int SquadId { get; private set; }
    public int InviationId { get; private set; }
    public long WorldCharKey { get; private set; }
    public sbyte RefuseType { get; private set; }

    public override void Read(PacketStream stream)
    {
        SquadId = stream.ReadInt32();
        InviationId = stream.ReadInt32();
        WorldCharKey = stream.ReadInt64();
        RefuseType = stream.ReadSByte();
    }
}
