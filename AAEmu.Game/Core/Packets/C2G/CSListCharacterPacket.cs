using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListCharacterPacket : GamePacket
    {
        public CSListCharacterPacket() : base(0x01f, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            Connection.LoadCharacters();

            var characters = Connection.Characters.Values.ToArray();

            foreach (var character in characters)
            {
                Connection.SendPacket(
                    new SCResponseUIDataPacket(character.Id, character.Name, "character_option", character.GetOption("character_option"))
                );
                Connection.SendPacket(
                    new SCResponseUIDataPacket(character.Id, character.Name, "key_binding", character.GetOption("key_binding"))
                );
            }

            Connection.SendPacket(new SCRaceCongestionPacket());

            if (characters.Length == 0)
                Connection.SendPacket(new SCCharacterListPacket(true, characters));
            else
                for (var i = 0; i < characters.Length; i += 2)
                {
                    var last = characters.Length - i <= 2;
                    var temp = new Character[last ? characters.Length - i : 2];
                    Array.Copy(characters, i, temp, 0, temp.Length);
                    Connection.SendPacket(new SCCharacterListPacket(last, temp));
                }
        }
    }
}