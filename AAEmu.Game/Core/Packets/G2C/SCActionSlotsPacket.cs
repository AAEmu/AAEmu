using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCActionSlotsPacket : GamePacket
    {
        private readonly ActionSlot[] _slots;

        public SCActionSlotsPacket(ActionSlot[] slots) : base(SCOffsets.SCActionSlotsPacket, 1)
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
                            _log.Error("SCActionSlotsPacket, Unknown ActionSlotType!");
                            break;
                        }
                }
            }

            return stream;
        }
    }
}
