using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFactionImmigrateInviteResultPacket(uint id, string charName, bool answer)
    : GamePacket(SCOffsets.SCFactionImmigrateInviteResultPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(charName);
        stream.Write(answer);
        return stream;
    }
}
