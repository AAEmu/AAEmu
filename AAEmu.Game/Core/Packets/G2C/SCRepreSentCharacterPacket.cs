using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRepreSentCharacterPacket(ulong charId, bool success, bool first, bool isDeleted)
    : GamePacket(SCOffsets.SCRepreSentCharacterPacket, 1)
{
    // Body: "type"(charId) i64 (ISerialize vtbl+0x98) + "success"/"first"/"isDeleted" bools (vtbl+0xF8).
    // character shown at server/character select. charId 0 with success=false means none selected.
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(charId);
        stream.Write(success);
        stream.Write(first);
        stream.Write(isDeleted);
        return stream;
    }
}
