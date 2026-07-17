using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppellationsPacket((uint id, bool active)[] appellations) : GamePacket(SCOffsets.SCAppellationsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(appellations.Length); // TODO max 512
        foreach (var (id, selected) in appellations)
        {
            stream.Write(id);
            stream.Write(selected);
        }

        return stream;
    }
}
