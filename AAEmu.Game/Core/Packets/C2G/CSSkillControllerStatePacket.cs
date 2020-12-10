using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSkillControllerStatePacket : GamePacket
    {
        public CSSkillControllerStatePacket() : base(CSOffsets.CSSkillControllerStatePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var scType = stream.ReadByte();
            if (scType == 0)
            {
                var len = stream.ReadSingle();
                var teared = stream.ReadBoolean();
                var cutouted = stream.ReadBoolean();
            }

            _log.Warn("SkillControllerState");
        }

        // TODO 
        /*
         *
              if ( a2->Reader->field_1C() )
              {
                a2->Reader->ReadByte("scType", &v7 + 3, 0);
                v2[1] = HIBYTE(v7);
              }
              else
              {
                HIBYTE(v7) = *(v2 + 4);
                a2->Reader->ReadByte("scType", &v7 + 3, 0);
              }
              if ( !v2[1] )
              {
                a2->Reader->ReadFloat("len", v2 + 2, 0);
                a2->Reader->ReadBool("teared", (v2 + 3), 0);
                a2->Reader->ReadBool("cutouted", v2 + 13, 0);
              }
         */
    }
}
