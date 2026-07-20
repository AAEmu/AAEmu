using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCFamilyMemberNameChangedPacket(uint familyId, uint charId, string newName)
    : GamePacket(SCOffsets.SCFamilyMemberNameChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(familyId);
        stream.Write(charId);
        stream.Write(newName);
        return stream;
    }
}
