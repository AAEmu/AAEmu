using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGimmicksRemovedPacket(uint[] ids) : GamePacket(SCOffsets.SCGimmicksRemovedPacket, 5)
{
    public const int MaxCountPerPacket = 500;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)ids.Length); // TODO max 500 elements
        foreach (var id in ids)
        {
            stream.WriteBc(id);
        }

        return stream;
    }
}
