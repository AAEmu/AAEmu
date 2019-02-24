using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Team
{
    public class Team : PacketMarshaler
    {
        public uint Id { get; set; }
        public bool Party { get; set; }
        public TeamMember[] Officers { get; set; } // TODO max length 10
        public TeamMember[] Members { get; set; } // TODO max length 50
        public LootingRule LootingRule { get; set; }

        public Team()
        {
            Officers = new TeamMember[0];
            Members = new TeamMember[50];
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Party);
            stream.Write((byte)Officers.Length);
            foreach (var officer in Officers)
                stream.Write(officer.Id);
            for (byte i = 0; i < 10; i++)
                stream.Write(i); // num
            foreach (var member in Members)
            {
                stream.Write(member?.Id ?? 0); // type(id)
                stream.Write(false); // con
            }

            for (var i = 0; i < 12; i++)
            {
                byte type = 1;
                stream.Write(type); // type: 1 -> uint, 2 -> bc
                if (type == 1)
                    stream.Write(0u);
                else if (type == 2)
                    stream.WriteBc(0);
            }

            stream.Write(LootingRule);
            return stream;
        }
        
        // TODO if i miss
        /*
         *    v2 = this;
              a2->Reader->ReadUInt32("id", this, 0);
              a2->Reader->ReadBool("party", v2 + 4, 0);
              a2->Reader->ReadByte("numOfficers", (v2 + 5), 0);
              v3 = 0;
              v17 = 10;
              for ( i = v2 + 8; ; i += 4 )
              {
                v5 = v2 + 5;
                if ( v2[5] > 10u )
                  v5 = &v17;
                if ( v3 >= *v5 )
                  break;
                a2->Reader->ReadUInt32("type", i, 0);
                ++v3;
              }
              v6 = 0;
              do
                a2->Reader->ReadByte("num", &v2[v6++ + 48], 0);
              while ( v6 < 10 );
              v7 = 0;
              v8 = v2 + 60;
              do
              {
                a2->Reader->ReadUInt32("type", v8, 0);
                a2->Reader->ReadBool("con", &v2[v7++ + 260], 0);
                v8 += 4;
              }
              while ( v7 < 50 );
              v9 = v2 + 312;
              v15 = 12;
              do
              {
                if ( a2->Reader->field_1C() )
                {
                  a2->Reader->ReadByte("type", &v16, 0);
                  *v9 = v16;
                }
                else
                {
                  v10 = a2->Reader->ReadByte;
                  v17 = *v9;
                  v10(a2, "type", &v17);
                }
                if ( *v9 == 1 )
                {
                  (a2->Reader->ReadUInt32)(a2, "type", v9 + 1, 0);
                }
                else if ( *v9 == 2 )
                {
                  if ( a2->Reader->field_1C() )
                    v9[1] = 0;
                  v11 = a2->Reader->field_1C();
                  v12 = a2->Reader;
                  if ( v11 )
                    v13 = v12->ReadBytes1;
                  else
                    v13 = v12->ReadBytes;
                  (v13)(a2, "bc", v9 + 1, 3);
                }
                v9 += 2;
                --v15;
              }
              while ( v15 );
              return LootingRuleRead(v2 + 102, a2);
         */
    }
}
