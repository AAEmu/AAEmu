using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCExpeditionRoleChangedPacket(uint id, byte role, string charName)
    : GamePacket(SCOffsets.SCExpeditionRoleChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(role);
        stream.Write(charName);
        return stream;
    }
}
