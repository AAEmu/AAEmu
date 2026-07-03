using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUpdateAdditionalSkillPointPacket() : GamePacket(SCOffsets.SCUpdateAdditionalSkillPointPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Additional (bonus) skill points available to the character; the reference sends 0 at world entry.
        stream.Write(0u);

        return stream;
    }
}
