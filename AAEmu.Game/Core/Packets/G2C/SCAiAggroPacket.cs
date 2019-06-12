using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAiAggroPacket : GamePacket
    {
        private readonly uint _npcId;
        private readonly int _count;
        private readonly uint _hostileUnitId;
        private readonly int _value;

        public SCAiAggroPacket(uint npcId, int count, uint hostileUnitId=0, int summarizeDamage=0) : base(SCOffsets.SCAiAggroPacket, 1)
        {
            _npcId = npcId;
            _count = count;
            _hostileUnitId = hostileUnitId;
            _value = summarizeDamage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_npcId);
            stream.Write(_count);

            if (_count > 0)
            {
                stream.WriteBc(_hostileUnitId);
                stream.Write(_value); // value 
                stream.Write(0);   // value
                stream.Write(0);   // value
                stream.Write((byte)135); // topFlags

                /*
                  v7 = (v4 + 8);
                  v17 = v4 + 8;
                  do
                  {
                    if ( a3->Reader->field_14("aggroValTbl", 1, v13) )
                    {
                      if ( a3->Reader->field_14("hostileUnitId", 1, v13) )
                      {
                        if ( a3->Reader->field_1C() )
                          *v7 = 0;
                        v15 = 3;
                        v8 = a3->Reader->field_1C() == 0;
                        v9 = a3->Reader;
                        v14 = "bc";
                        if ( v8 )
                          v10 = v9->ReadBytes;
                        else
                          v10 = v9->ReadBytes1;
                        v10(a3);
                        a3->Reader->field_18(a3);
                      }
                      v11 = (v7 + 1);
                      if ( a3->Reader->field_14("aggro", 1, v14) )
                      {
                        v12 = 3;
                        do
                        {
                          a3->Reader->ReadUInt32("value", v11, 0);
                          v11 += 4;
                          --v12;
                        }
                        while ( v12 );
                        a3->Reader->ReadByte("topFlags", (v17 + 16), 0);
                        a3->Reader->field_18(a3);
                        v7 = v17;
                      }
                      a3->Reader->field_18(a3);
                      v5 = v15;
                    }
                    result = v16 + 1;
                    v7 += 5;
                    v16 = result;
                    v17 = v7;
                  }
                  while ( result < *v5 );
                 */
            }

            return stream;
        }
    }
}
