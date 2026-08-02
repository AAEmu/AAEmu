using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// that id as the key in the outgoing map when isReceive is false and in the incoming map when
/// it is true, proving that the value identifies the counterpart.
/// </remarks>
public class CSFriendCancelPacket() : GamePacket(CSOffsets.CSFriendCancelPacket, 1)
{
    public bool IsReceive { get; private set; }
    public ulong CounterpartCharacterId { get; private set; }

    public override void Read(PacketStream stream)
    {
        IsReceive = stream.ReadBoolean();
        CounterpartCharacterId = stream.ReadUInt64();
    }

    public override void Execute()
    {
        FriendMananger.Instance.CancelFriend(Connection.ActiveChar, IsReceive, CounterpartCharacterId);
    }
}
