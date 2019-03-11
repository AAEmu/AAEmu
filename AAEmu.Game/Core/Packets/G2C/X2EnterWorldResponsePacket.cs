using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class X2EnterWorldResponsePacket : GamePacket
    {
        private readonly short _reason;
        private readonly uint _token;
        private readonly ushort _port;
        private readonly bool _gm;

        public X2EnterWorldResponsePacket(short reason, bool gm, uint token, ushort port) : base(SCOffsets.X2EnterWorldResponsePacket, 1)
        {
            _reason = reason;
            _token = token;
            _port = port;
            _gm = gm;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_reason);
            stream.Write(_gm);
            stream.Write(_token); // Stream Token // cs
            stream.Write(_port); // Stream Port // cp
            stream.Write(Helpers.UnixTimeNow()); // wf
            stream.Write((ushort)0); // pubKeySize
            stream.Write(""); // pubKey
            return stream;
        }

        // TODO if i miss data
        /*
              if ( a2->Reader->field_1C() )
              {
                a2->Reader->ReadUInt16(a2, "reason", &v7, 0);
                *(v2 + 3) = v7;
              }
              else
              {
                v7 = *(v2 + 6);
                a2->Reader->ReadUInt16(a2, "reason", &v7, 0);
              }
              a2->Reader->ReadBool("gm", v2 + 16, 0);
              a2->Reader->ReadInt32("sc", v2 + 20, 0);
              a2->Reader->ReadUInt16(a2, "sp", v2 + 24, 0);
              a2->Reader->ReadUInt64("wf", v2 + 32, 0);
              a2->Reader->ReadUInt16(a2, "pubKeySize", v2 + 4136, 0);
              v3 = *(v2 + 2068);
              v6 = v2 + 40;
              v4 = a2->Reader->field_1C() == 0;
              v5 = a2->Reader;
              if ( v4 )
                v5->ReadString1("pubKey", v6, v3);
              else
                v5->ReadString("pubKey", v6, v3);
         */
    }
}
