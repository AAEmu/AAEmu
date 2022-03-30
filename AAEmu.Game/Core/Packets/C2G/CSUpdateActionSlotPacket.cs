using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUpdateActionSlotPacket : GamePacket
    {
        public CSUpdateActionSlotPacket() : base(CSOffsets.CSUpdateActionSlotPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slot = stream.ReadByte();
            var type = (ActionSlotType)stream.ReadByte();

            switch (type)
            {
                case ActionSlotType.None:
                    Connection.ActiveChar.SetAction(slot, ActionSlotType.None, 0);
                    break;
                case ActionSlotType.ItemType:
                case ActionSlotType.Spell:
                case ActionSlotType.RidePetSpell:
                case ActionSlotType.BattlePetSpell:
                    var actionId = stream.ReadUInt32();
                    Connection.ActiveChar.SetAction(slot, type, actionId);
                    break;
                case ActionSlotType.ItemId:
                    var itemId = stream.ReadUInt64();
                    Connection.ActiveChar.SetAction(slot, type, itemId);
                    break;
                default:
                    _log.Error("UpdateActionSlot, Unknown ActionSlotType!");
                    break;
            }
        }
    }
}
