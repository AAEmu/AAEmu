using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCActionSlotsPacket : GamePacket
    {
        private readonly ActionSlot[] _slots;

        public SCActionSlotsPacket(ActionSlot[] slots) : base(SCOffsets.SCActionSlotsPacket, 5)
        {
            _slots = slots;
        }

        public override PacketStream Write(PacketStream stream)
        {
            foreach (var slot in _slots) // in 1.2 ... 2.0 = 85
            {
                stream.Write((byte)slot.Type);
                switch (slot.Type)
                {
                    case ActionSlotType.None:
                    case ActionSlotType.Unk3:
                        break;
                    case ActionSlotType.Item:
                    case ActionSlotType.Skill:
                    case ActionSlotType.Unk5:
                        stream.Write(slot.ActionId);
                        break;
                    case ActionSlotType.Unk4:
                        stream.Write(slot.ItemId);
                        break;
                    default:
                        _log.Error("SCActionSlotsPacket, Unknown ActionSlotType!");
                        break;
                }
            }

            return stream;
        }

        // TODO if i miss data
    }
}
