using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionOwnerChangedPacket(uint id, uint id2, string charName)
    : GamePacket(SCOffsets.SCExpeditionOwnerChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(id2);
        stream.Write(charName);
        return stream;
    }
}
