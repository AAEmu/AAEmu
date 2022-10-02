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

        public ACWorldListPacket(List<GameServer> gs, List<LoginCharacterInfo> characters) : base(LCOffsets.ACWorldListPacket)
        {
            _gs = gs;
            _characters = characters;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_gs.Count);
            foreach (var gs in _gs)
            {
                stream.Write(gs.Id);
                stream.Write(gs.Name);
                stream.Write(gs.Active);
                if (gs.Active)
                {
                    stream.Write((byte) gs.Load); // con
                    for (var i = 0; i < 9; i++) // race
                        stream.Write((byte) 0); // rcon
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
                     RACE_CONGESTION = {
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

            stream.Write((byte)_characters.Count);
            if(_characters.Count > 0)
            {
                foreach(var character in _characters)
                {
                    stream.Write(character.AccountId);
                    stream.Write(character.GsId);
                    stream.Write(character.Id);
                    stream.Write(character.Name);
                    stream.Write(character.Race);
                    stream.Write(character.Gender);
                    stream.Write(new byte[16], true); // guid
                    stream.Write(0L); // v
                }
            }
            return stream;
        }
    }
}
