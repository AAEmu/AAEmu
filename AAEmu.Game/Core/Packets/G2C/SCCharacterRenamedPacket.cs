using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterRenamedPacket(uint characterId, string oldName, string newName)
    : GamePacket(SCOffsets.SCCharacterRenamedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(characterId);
        stream.Write(oldName); // TODO max length 128
        stream.Write(newName); // TODO max length 128
        return stream;
    }
}
