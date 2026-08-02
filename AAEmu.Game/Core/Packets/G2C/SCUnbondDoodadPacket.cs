using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// bc(character), u64(characterId), bc(doodad). The middle field is 64-bit — the packet
/// class holds it at +0x18 with the trailing bc at +0x20.
/// </summary>
public class SCUnbondDoodadPacket(uint characterObjId, uint characterId, uint doodadObjId)
    : GamePacket(SCOffsets.SCUnbondDoodadPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(characterObjId);
        stream.Write((ulong)characterId);
        stream.WriteBc(doodadObjId);
        return stream;
    }
}
