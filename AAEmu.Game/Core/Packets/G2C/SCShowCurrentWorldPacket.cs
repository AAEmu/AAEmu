using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCShowCurrentWorldPacket(byte worldId) : GamePacket(SCOffsets.SCShowCurrentWorldPacket, 1)
{
    // Sent while the context view is at SELECT_CHARACTER (state 2) to open the in-world data load, ahead of the
    // server-driven ChangeState(3→7). Capture value = the character's current world id.
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldId);
        return stream;
    }
}
