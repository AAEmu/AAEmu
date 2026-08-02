using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// compact object IDs.
/// </summary>
public class SCGimmicksRemovedPacket : GamePacket
{
    public const int MaxCountPerPacket = 500;

    private readonly uint[] _ids;

    public SCGimmicksRemovedPacket(uint[] ids)
        : base(SCOffsets.SCGimmicksRemovedPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Length > MaxCountPerPacket)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ids),
                ids.Length,
                $"The native client accepts at most {MaxCountPerPacket} gimmicks per remove packet.");
        }

        _ids = ids;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)_ids.Length);
        foreach (var id in _ids)
        {
            stream.WriteBc(id);
        }

        return stream;
    }
}
