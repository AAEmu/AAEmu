using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// </remarks>
public class CSFriendRequestPacket() : GamePacket(CSOffsets.CSFriendRequestPacket, 1)
{
    public string TargetName { get; private set; }

    public override void Read(PacketStream stream)
    {
        TargetName = stream.ReadString();
    }

    public override void Execute()
    {
        FriendMananger.Instance.RequestFriend(Connection.ActiveChar, TargetName);
    }
}
