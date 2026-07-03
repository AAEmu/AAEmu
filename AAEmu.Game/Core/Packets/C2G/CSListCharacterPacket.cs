using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSListCharacterPacket() : GamePacket(CSOffsets.CSListCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var size = stream.ReadInt32(); // TODO max size 4096
        var data = stream.ReadBytes(); // TODO or string?

        Connection.SendPacket(new SCGetSlotCountPacket(4)); // available character slots (matches the login path)
        Connection.SendPacket(
            new SCAccountInfoPacket(
                (int)Connection.Payment.Method,
                Connection.Payment.Location,
                Connection.Payment.StartTime,
                Connection.Payment.EndTime
            )
        );

        Connection.LoadAccount();

        var characters = Connection.Characters.Values.ToArray();

        //            foreach (var character in characters)
        //            {
        //                Connection.SendPacket(
        //                    new SCResponseUIDataPacket(character.Id, character.Name, "character_option", character.GetOption("character_option"))
        //                );
        //                Connection.SendPacket(
        //                    new SCResponseUIDataPacket(character.Id, character.Name, "key_binding", character.GetOption("key_binding"))
        //                );
        //            }

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

        // Featured/representative character for the character-select screen. The reference sends
        // SCRepreSentCharacter (0x2C4) right after the character list; represent the first character
        // (success/first true), or an empty representation when the account has none.
        if (characters.Length > 0)
            Connection.SendPacket(new SCRepreSentCharacterPacket(characters[0].Id, true, true, false));
        else
            Connection.SendPacket(new SCRepreSentCharacterPacket(0, false, false, false));

        var houses = Connection.Houses.Values.ToArray();
        foreach (var house in houses)
            Connection.SendPacket(new SCLoginCharInfoHouse(house.OwnerId, house));
    }
}
