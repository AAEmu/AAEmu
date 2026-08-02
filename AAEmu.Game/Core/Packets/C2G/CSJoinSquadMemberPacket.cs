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
public class CSJoinSquadMemberPacket() : GamePacket(CSOffsets.CSJoinSquadMemberPacket, 1)
{
    public int SquadId { get; private set; }
    public int TypeValue { get; private set; }
    public int InvitationId { get; private set; }
    public int JoinKey { get; private set; }

    public override void Read(PacketStream stream)
    {
        SquadId = stream.ReadInt32();
        TypeValue = stream.ReadInt32();
        InvitationId = stream.ReadInt32();
        JoinKey = stream.ReadInt32();
    }
}
