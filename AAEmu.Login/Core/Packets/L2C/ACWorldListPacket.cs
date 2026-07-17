using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client containing the list of available game servers and character
/// information.
/// </summary>
/// <param name="gameServers">The list of game servers.</param>
/// <param name="characters">The list of characters belonging to the account across all game servers.</param>
public class ACWorldListPacket(List<GameServer> gameServers, List<LoginCharacterInfo> characters)
    : LoginPacket(LCOffsets.ACWorldListPacket)
{
    private readonly byte _title = 0x01; // 01-FRESH, 02-EVO, 03-WAR, 
    private readonly byte _color = 0x01; // 02

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)gameServers.Count);
        foreach (var gs in gameServers)
        {
            stream.Write(gs.Id.Value);
            stream.Write(_title); // надпись в списке серверов 00-нет надписи, 01- НОВЫЙ, 02-ОБЪЕДИНЕННЫЙ, 03-ОБЪЕДИНЕННЫЙ, 04-нет надписи
            stream.Write(_color); // цвет надписи в списке серверов 00-синий, 01- зеленый, 02-фиолетовый, 03, 04, 08-красный, 0x10-
            stream.Write(gs.Name);
            stream.Write(gs.Active);
            if (gs.Active)
            {
                // Server Status - 0x00 - normal / 0x01 - load / 0x02 - queue
                stream.Write((byte)gs.Load); // con
                for (var i = 0; i < 9; i++) // race 9 in 1.2, 3+, 10 in 5+
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

        stream.Write((byte)characters.Count);
        if (characters.Count > 0)
        {
            foreach (var character in characters)
            {
                stream.Write(character.AccountId); // accountId
                stream.Write(character.GsId);      // worldId
                stream.Write(character.Id);        // charId
                stream.Write(character.Name);      // name
                stream.Write(character.Race);      // CharRace
                stream.Write(character.Gender);    // CharGender
                stream.Write(new byte[16], true); // guid
                stream.Write(0L); // v
            }
        }
        return stream;
    }
}
