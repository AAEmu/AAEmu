using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// the full gimmick records.
/// </summary>
public class SCGimmicksCreatedPacket : GamePacket
{
    public const int MaxCountPerPacket = 30;

    private readonly Gimmick[] _gimmicks;

    public SCGimmicksCreatedPacket(Gimmick[] gimmicks)
        : base(SCOffsets.SCGimmicksCreatedPacket, 1)
    {
        ArgumentNullException.ThrowIfNull(gimmicks);
        if (gimmicks.Length > MaxCountPerPacket)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gimmicks),
                gimmicks.Length,
                $"The native client accepts at most {MaxCountPerPacket} gimmicks per create packet.");
        }

        _gimmicks = gimmicks;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_gimmicks.Length);
        foreach (var gimmick in _gimmicks)
        {
            gimmick.Write(stream);
        }

        return stream;
    }
}
