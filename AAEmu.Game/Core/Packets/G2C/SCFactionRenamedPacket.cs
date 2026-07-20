using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionRenamedPacket(uint id, string name, bool byGm) : GamePacket(SCOffsets.SCFactionRenamedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(name);
        stream.Write(byGm);
        return stream;
    }
}
