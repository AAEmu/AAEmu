using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

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
        SquadManager.Instance.Join(Connection.ActiveChar, (uint)SquadId, (uint)TypeValue, InvitationId, JoinKey);
    }
}
