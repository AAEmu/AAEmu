using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAiAggroPacket : GamePacket
    {
        private readonly uint _npcId;
        private readonly int _count;
        
        public SCAiAggroPacket(uint npcId) : base(0x1ba, 1)
        {
            _npcId = npcId;
            _count = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_npcId);
            stream.Write(_count);

            if (_count > 0)
            {
                /*
                  v7 = (_DWORD *)(v4 + 8);
                  v16 = v4 + 8;
                  do
                  {
                    if ( ((unsigned __int8 (__stdcall *)(const char *, signed int))a3->Reader->field_14)("aggroValTbl", 1) )
                    {
                      if ( ((unsigned __int8 (__thiscall *)(struc_1 *, const char *, signed int, signed int))a3->Reader->field_14)(
                             a3,
                             "hostileUnitId",
                             1,
                             v13) )
                      {
                        if ( a3->Reader->field_1C() )
                          *v7 = 0;
                        v14 = 3;
                        v8 = a3->Reader->field_1C() == 0;
                        v9 = a3->Reader;
                        if ( v8 )
                          v10 = v9->ReadBytes;
                        else
                          v10 = (struc_2 *)v9->ReadBytes1;
                        ((void (__thiscall *)(struc_1 *, char **, _DWORD *))v10)(a3, &StringBC, v7);
                        ((void (__thiscall *)(struc_1 *))a3->Reader->field_18)(a3);
                      }
                      v13 = 1;
                      v11 = (char *)(v7 + 1);
                      if ( ((unsigned __int8 (__thiscall *)(struc_1 *, const char *))a3->Reader->field_14)(a3, "aggro") )
                      {
                        v12 = 3;
                        do
                        {
                          a3->Reader->ReadUInt32("value", v11, 0);
                          v11 += 4;
                          --v12;
                        }
                        while ( v12 );
                        a3->Reader->ReadByte("topFlags", (unsigned __int8 *)(v16 + 16), 0);
                        ((void (__thiscall *)(struc_1 *))a3->Reader->field_18)(a3);
                        v7 = (_DWORD *)v16;
                      }
                      ((void (__thiscall *)(struc_1 *))a3->Reader->field_18)(a3);
                      v5 = (_DWORD *)v14;
                    }
                    result = v15 + 1;
                    v7 += 5;
                    v15 = result;
                    v16 = (int)v7;
                  }
                  while ( result < *v5 );
                 */
            }
            
            return stream;
        }
    }
}