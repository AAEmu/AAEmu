using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyPriestBuffPacket : GamePacket
    {
        public CSBuyPriestBuffPacket() : base(CSOffsets.CSBuyPriestBuffPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var priestBuffId = stream.ReadUInt32();
            var npcUnitId = stream.ReadBc();

            _log.Warn("BuyPriestBuff, PriestBuffId: {0}, NpcUnitId: {1}", priestBuffId, npcUnitId);
        }

        // TODO if i miss
        /*
          if ( !a2->Reader->field_14("priestBuffType", 1, v4) )
            return ReadBc_2(a2, (v2 + 3), "npcUnitId", v2 + 3, 0);
          a2->Reader->ReadUInt32("type", v2 + 2, 0);
          a2->Reader->field_18(a2);
          return ReadBc_2(a2, (v2 + 3), "npcUnitId", v2 + 3, 0);
         */
    }
}
