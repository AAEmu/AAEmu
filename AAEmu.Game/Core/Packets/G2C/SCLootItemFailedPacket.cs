using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: an unnamed u16, u16 ErrorMessage, i64 iid, i32 type, bc.
/// 1.2 wrote the error as an i32, then an item index and a padding byte, and finished with the template id —
/// a different shape and a different length, so nothing after it in the stream lined up. The client wants
/// the item's own id, not its template.
/// </remarks>
public class SCLootItemFailedPacket(ErrorMessageType errorMessage, LootOwnerType lootOwnerType, uint lootOwnerObjId, ulong itemId, ushort unnamed1 = 0) : GamePacket(SCOffsets.SCLootItemFailedPacket, 1)
{
    private readonly ushort _errorMessage = (ushort)errorMessage;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(unnamed1);
        stream.Write(_errorMessage);
        stream.Write((long)itemId);
        stream.Write((int)lootOwnerType);
        stream.WriteBc(lootOwnerObjId);
        return stream;
    }
}
