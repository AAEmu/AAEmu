using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadsRemovedPacket(bool last, uint[] ids) : GamePacket(SCOffsets.SCDoodadsRemovedPacket, 5)
{
    public const int MaxCountPerPacket = 400; // Suggested Maximum Size

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)ids.Length);
        stream.Write(last);
        foreach (var id in ids)
        {
            stream.WriteBc(id);
            stream.Write(false); // e  if false then the doodad will be deleted
        }

        return stream;
    }
}
