using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterRenamedPacket(ulong type, bool success, sbyte worldId, string oldName, string newName)
    : GamePacket(SCOffsets.SCCharacterRenamedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write(success);
        if (success)
        {
            stream.Write(worldId);
            stream.Write(oldName);
            stream.Write(newName);
        }

        return stream;
    }
}
