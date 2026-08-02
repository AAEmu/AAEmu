using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppellationsPacket(IReadOnlyList<(int Id, sbyte Selected)> appellations)
    : GamePacket(SCOffsets.SCAppellationsPacket, 1)
{
    public const int MaximumEntries = 1024;

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(appellations.Count, MaximumEntries);
        stream.Write((uint)count);
        for (var i = 0; i < count; i++)
        {
            stream.Write(appellations[i].Id);
            stream.Write(appellations[i].Selected);
        }

        return stream;
    }
}
