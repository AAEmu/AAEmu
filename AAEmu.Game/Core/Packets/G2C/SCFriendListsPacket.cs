using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFriendListsPacket : GamePacket
{
    private readonly int _total;
    private readonly Friend[] _friends;

    public SCFriendListsPacket(int total, Friend[] friends) : base(SCOffsets.SCFriendListsPacket, 5)
    {
        _total = total;
        _friends = friends;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_friends.Length); // TODO max length 200
        foreach (var friend in _friends)
            stream.Write(friend);
        return stream;
    }
}
