using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUpdateActionSlotPacket : GamePacket
    {
        public CSUpdateActionSlotPacket() : base(CSOffsets.CSUpdateActionSlotPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slot = stream.ReadByte();
            var type = (ActionSlotType) stream.ReadByte();

            switch (type)
            {
                case ActionSlotType.None:
                    Connection.ActiveChar.SetAction(slot, ActionSlotType.None, 0);
                    break;
                case ActionSlotType.Item:
                case ActionSlotType.Skill:
                    // TODO убрать что бы найти что это ... case ActionSlotType.Unk5:
                    var actionId = stream.ReadUInt32();
                    Connection.ActiveChar.SetAction(slot, type, actionId);
                    break;
                case ActionSlotType.Unk4:
                    var itemId = stream.ReadUInt64();
                    // TODO
                    break;
                default:
                    _log.Error("UpdateActionSlot, Unknown packet type!");
                    break;
            }

//            if (type == 1 || type == 2 || type == 5)
//            {
//                stream.ReadUInt32();
//            }
//            else if (type == 4)
//            {
//                stream.ReadUInt64();
//            }
        }
    }
}
