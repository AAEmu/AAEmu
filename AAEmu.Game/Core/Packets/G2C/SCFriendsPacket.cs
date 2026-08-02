using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCFriendsPacket : GamePacket
{
    public const int MaxCountPerPacket = 20;

    private readonly int _total;
    private readonly Friend[] _friends;

    public SCFriendsPacket(int total, Friend[] friends)
        : base(SCOffsets.SCFriendsPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(friends);
        if (total < friends.Length)
            throw new ArgumentOutOfRangeException(nameof(total), total, "Total cannot be smaller than the page count.");
        if (friends.Length > MaxCountPerPacket)
        {
            throw new ArgumentOutOfRangeException(
                nameof(friends),
                friends.Length,
                $"The native client accepts at most {MaxCountPerPacket} friends per packet.");
        }

        _total = total;
        _friends = friends;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_friends.Length);
        foreach (var friend in _friends)
            stream.Write(friend);
        return stream;
    }
}
