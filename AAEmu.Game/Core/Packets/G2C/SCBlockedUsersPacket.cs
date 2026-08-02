using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
public class SCBlockedUsersPacket : GamePacket
{
    public const int MaxCountPerPacket = 500;

    private readonly int _total;
    private readonly Blocked[] _blocked;

    public SCBlockedUsersPacket(int total, Blocked[] blocked)
        : base(SCOffsets.SCBlockedUsersPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(blocked);
        if (total < blocked.Length)
            throw new ArgumentOutOfRangeException(nameof(total), total, "Total cannot be smaller than the page count.");
        if (blocked.Length > MaxCountPerPacket)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blocked),
                blocked.Length,
                $"The native client accepts at most {MaxCountPerPacket} blocked users per packet.");
        }

        _total = total;
        _blocked = blocked;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_total);
        stream.Write(_blocked.Length);
        foreach (var blocked in _blocked)
            stream.Write(blocked);
        return stream;
    }
}
