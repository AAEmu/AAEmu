using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// and the bool success field.
/// </summary>
public class SCSearchListPacket : GamePacket
{
    public const int MaxCountPerPacket = 20;

    private readonly int _total;
    private readonly Friend[] _friends;
    private readonly bool _success;

    public SCSearchListPacket(int total, Friend[] friends, bool success)
        : base(SCOffsets.SCSearchListPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(friends);
        if (total < friends.Length)
            throw new ArgumentOutOfRangeException(nameof(total), total, "Total cannot be smaller than the page count.");
        if (friends.Length > MaxCountPerPacket)
        {
            throw new ArgumentOutOfRangeException(
                nameof(friends),
                friends.Length,
                $"The native client accepts at most {MaxCountPerPacket} search results per packet.");
        }

        _total = total;
        _friends = friends;
        _success = success;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_friends.Length);
        foreach (var friend in _friends)
            stream.Write(friend);
        stream.Write(_success);
        return stream;
    }
}
