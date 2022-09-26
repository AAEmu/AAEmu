using System.Collections.Generic;
using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACWorldListPacket : LoginPacket
    {
        private readonly List<GameServer> _gs;
        private readonly List<LoginCharacterInfo> _characters;
        private readonly byte _title;
        private readonly byte _color;

        public ACWorldListPacket(List<GameServer> gs, List<LoginCharacterInfo> characters) : base(LCOffsets.ACWorldListPacket)
        {
            _gs = gs;
            _characters = characters;
            _title = 01; //01-FRESH, 02-EVO, 03-WAR, 
            _color = 01; //02
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_gs.Count);
            foreach (var gs in _gs)
            {
                stream.Write(gs.Id);
                stream.Write(_title); // надпись в списке серверов 00-нет надписи, 01- НОВЫЙ, 02-ОБЪЕДИНЕННЫЙ, 03-ОБЪЕДИНЕННЫЙ, 04-нет надписи
                stream.Write(_color); // цвет надписи в списке серверов 00-синий, 01- зеленый, 02-фиолетовый, 03, 04, 08-красный, 0x10-
                //stream.Write((byte)0); // add for 5.1
                stream.Write(gs.Name);
                stream.Write(gs.Active);
                if (gs.Active)
                {
                    //Server Status - 0x00 - normal / 0x01 - load / 0x02 - queue
                    stream.Write((byte)gs.Load); // con
                    //The following sections are the racial restrictions on server creation for this server selection interface 0 Normal 2 Prohibited
                    for (var i = 0; i < 9; i++) // race
                    //for (var i = 0; i < 10; i++) // race //add for 5.1
                    {
                        stream.Write((byte)0); // rcon

                        /*
                         RACE_NONE = 0,
                         RACE_NUIAN = 1,
                         RACE_FAIRY = 2,
                         RACE_DWARF = 3,
                         RACE_ELF = 4,
                         RACE_HARIHARAN = 5,
                         RACE_FERRE = 6,
                         RACE_RETURNED = 7,
                         RACE_WARBORN = 8
                          */
                        /*
                             RACE_CONGESTION = 
                             {
                            LOW = 0,
                            MIDDLE = 1,
                            HIGH = 2,
                            FULL = 3,
                            PRE_SELECT_RACE_FULL = 9,
                            CHECK = 10
                         }
                        */
                    }
                }
            }

            stream.Write((byte)_characters.Count);
            if (_characters.Count > 0)
            {
                foreach (var character in _characters)
                {
                    stream.Write(character.AccountId);
                    stream.Write(character.GsId);
                    stream.Write(character.Id);
                    stream.Write(character.Name);
                    stream.Write(character.Race);
                    stream.Write(character.Gender);
                    stream.Write(new byte[16], true); //guid
                    stream.Write(0L); //v
                }
            }
            return stream;
        }
    }
}
