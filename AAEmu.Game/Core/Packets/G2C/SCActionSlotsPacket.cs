using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActionSlotsPacket(ActionSlot[] slots) : GamePacket(SCOffsets.SCActionSlotsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        foreach (var s in slots)
        {
            var slot = (byte)s.Type;
            stream.Write(slot);
            switch (s.Type)
            {
                case ActionSlotType.None:
                    {
                        break;
                    }
                case ActionSlotType.ItemType:
                case ActionSlotType.Spell:
                case ActionSlotType.RidePetSpell:
                    {
                        stream.Write((uint)s.ActionId);
                        break;
                    }
                case ActionSlotType.ItemId:
                    {
                        stream.Write(s.ActionId); // itemId
                        break;
                    }
                default:
                    {
                        Logger.Error("SCActionSlotsPacket, Unknown ActionSlotType!");
                        break;
                    }
            }
        }

        return stream;
    }
}
