using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCShowCurrentWorldPacket(byte worldId) : GamePacket(SCOffsets.SCShowCurrentWorldPacket, 1)
{
    // Body: single "worldId" byte. x2game-dev_dedicate SCShowCurrentWorldPacket::Serialize (sub_39C1BF20)
    // emits Value("worldId", obj+16) via the ISerialize u8 slot (vtbl+0x90). Opcode 0x354 (sub_3955F450).
    // Sent while the context view is at SELECT_CHARACTER (state 2) to open the in-world data load, ahead of the
    // server-driven ChangeState(3→7). Capture value = the character's current world id.
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldId);
        return stream;
    }
}
