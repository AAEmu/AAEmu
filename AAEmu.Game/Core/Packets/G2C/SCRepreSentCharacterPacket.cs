using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Names the account's main ("represent") character. The odd capitalisation is the client's own -
/// its RTTI reads ".?AUSCRepreSentCharacterPacket@@".
/// </summary>
/// <remarks>
/// Client layout (read at RVA 0xC62F10):
///
///   type      u64    the character id
///   success   bool
///   first     bool
///   isDeleted bool
///
/// The handler (RVA 0x4DD270) is short and decides everything:
///
///     if (packet.success) globals[0x3698] = packet.type;
///     notifyUi(success, first, isDeleted);
///
/// So <paramref name="success"/> is what makes the client adopt the id - and a packet sent with it set
/// is not a status report, it is an instruction. GetRepresentCharacterIndex (RVA 0x9A57E0) then walks
/// the character array comparing ids and returns a 1-based index, or 0 when the stored id matches
/// nothing. That index is what the character-select UI checks before allowing a deletion.
///
/// Send success=false to say "no main character": the handler then leaves the stored id alone, and it
/// starts at zero because the field lives in zero-initialised data.
/// </remarks>
public class SCRepreSentCharacterPacket(ulong charId, bool success, bool first, bool isDeleted)
    : GamePacket(SCOffsets.SCRepreSentCharacterPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(charId);       // u64  type
        stream.Write(success);      // bool success
        stream.Write(first);        // bool first
        stream.Write(isDeleted);    // bool isDeleted
        return stream;
    }
}
