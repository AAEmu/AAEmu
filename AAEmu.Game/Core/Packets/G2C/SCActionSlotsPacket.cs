using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActionSlotsPacket : GamePacket
{
    private readonly ActionSlot[] _slots;

    public SCActionSlotsPacket(ActionSlot[] slots) : base(SCOffsets.SCActionSlotsPacket, 5)
    {
        _slots = slots;
    }

    public override PacketStream Write(PacketStream stream)
    {
        foreach (var s in _slots)
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
                case ActionSlotType.BattlePetSpell:
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
