using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionImmigrateInvitePacket(uint id, uint id2) : GamePacket(SCOffsets.SCFactionImmigrateInvitePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        return stream;
    }
}
