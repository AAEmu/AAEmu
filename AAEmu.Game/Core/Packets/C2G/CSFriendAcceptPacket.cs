using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// The 10.0.2.13 client serializer writes a u64 typed id. Its pending-request lookup in
/// </remarks>
public class CSFriendAcceptPacket() : GamePacket(CSOffsets.CSFriendAcceptPacket, 1)
{
    public ulong RequesterCharacterId { get; private set; }

    public override void Read(PacketStream stream)
    {
        RequesterCharacterId = stream.ReadUInt64();
    }

    public override void Execute()
    {
        FriendMananger.Instance.AcceptFriend(Connection.ActiveChar, RequesterCharacterId);
    }
}
