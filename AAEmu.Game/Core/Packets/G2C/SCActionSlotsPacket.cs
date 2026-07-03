using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActionSlotsPacket(ActionSlot[] slots) : GamePacket(SCOffsets.SCActionSlotsPacket, 1)
{
    // 10.0.2.13: the client deserializer (x2game-dev_dedicate SCActionSlots serializer sub_39C3A2A0) reads a
    // FIXED count of slots with no length prefix — a short packet leaves it "not enough buffer for type" and
    // crashes with a serializer size mismatch. Each slot is type(u8) + payload by type:
    // 1/2/5/6 -> u32 actionId, 4 -> i64 itemId, everything else -> no payload.
    private const int ClientSlotCount = 217;

    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < ClientSlotCount; i++)
        {
            var s = i < slots.Length ? slots[i] : null;
            var type = s?.Type ?? ActionSlotType.None;
            stream.Write((byte)type);
            switch (type)
            {
                case ActionSlotType.ItemType:
                case ActionSlotType.Spell:
                case ActionSlotType.RidePetSpell:
                    stream.Write((uint)s.ActionId);
                    break;
                case ActionSlotType.ItemId:
                    stream.Write(s.ActionId); // itemId (i64)
                    break;
            }
        }

        return stream;
    }
}
