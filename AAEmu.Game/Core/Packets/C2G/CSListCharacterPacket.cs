using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSListCharacterPacket : GamePacket
    {
        public CSListCharacterPacket() : base(0x020, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var size = stream.ReadInt32(); // TODO max size 4096
            var data = stream.ReadBytes(); // TODO or string?

            DbLoggerCategory.Database.Connection.SendPacket(new SCGetSlotCountPacket(0));
            DbLoggerCategory.Database.Connection.SendPacket(
                new SCAccountInfoPacket(
                    (int)DbLoggerCategory.Database.Connection.Payment.Method,
                    DbLoggerCategory.Database.Connection.Payment.Location,
                    DbLoggerCategory.Database.Connection.Payment.StartTime,
                    DbLoggerCategory.Database.Connection.Payment.EndTime
                )
            );

            DbLoggerCategory.Database.Connection.LoadAccount();

            var characters = DbLoggerCategory.Database.Connection.Characters.Values.ToArray();

//            foreach (var character in characters)
//            {
//                Connection.SendPacket(
//                    new SCResponseUIDataPacket(character.Id, character.Name, "character_option", character.GetOption("character_option"))
//                );
//                Connection.SendPacket(
//                    new SCResponseUIDataPacket(character.Id, character.Name, "key_binding", character.GetOption("key_binding"))
//                );
//            }

            DbLoggerCategory.Database.Connection.SendPacket(new SCRaceCongestionPacket());

            if (characters.Length == 0)
                DbLoggerCategory.Database.Connection.SendPacket(new SCCharacterListPacket(true, characters));
            else
                for (var i = 0; i < characters.Length; i += 2)
                {
                    var last = characters.Length - i <= 2;
                    var temp = new Character[last ? characters.Length - i : 2];
                    Array.Copy(characters, i, temp, 0, temp.Length);
                    DbLoggerCategory.Database.Connection.SendPacket(new SCCharacterListPacket(last, temp));
                }

            var houses = DbLoggerCategory.Database.Connection.Houses.Values.ToArray();
            foreach (var house in houses)
                DbLoggerCategory.Database.Connection.SendPacket(new SCLoginCharInfoHouse(house.OwnerId, house));
        }
    }
}
