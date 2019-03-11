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
            foreach (var slot in _slots)
            {
                stream.Write((byte)slot.Type);
                if (slot.Type != ActionSlotType.None)
                    stream.Write(slot.ActionId);
            }

            return stream;
        }
        
        // TODO if i miss data
        /*
              v3 = 85;
              do
              {
                if ( a2->Reader->field_1C() )
                {
                  a2->Reader->ReadByte("type", &v6, 0);
                  *(v2 - 2) = v6;
                }
                else
                {
                  v4 = a2->Reader->ReadByte;
                  v7 = *(v2 - 8);
                  v4(a2, "type", &v7);
                }
                result = *(v2 - 2) - 1;
                switch ( *(v2 - 2) )
                {
                  case 1:
                  case 2:
                  case 5:
                    result = a2->Reader->ReadUInt32("type", v2, 0);
                    break;
                  case 4:
                    result = (a2->Reader->ReadUInt64)("itemId", v2, 0);
                    break;
                  default:
                    break;
                }
                v2 += 16;
                --v3;
              }
              while ( v3 );
         */
    }
}
