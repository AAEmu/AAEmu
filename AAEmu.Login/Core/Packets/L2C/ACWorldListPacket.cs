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
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(false); // privacyPolicyState

        stream.Write((byte)gameServers.Count);
        foreach (var gs in gameServers)
        {
            stream.Write(gs.Id.Value);        // id
            stream.Write((byte)0);            // parentId (mirror parent server; 0 = primary)
            stream.Write((ushort)0);          // type
            stream.Write((byte)0);            // color
            stream.Write(gs.Name);            // name
            stream.Write((byte)(gs.Active ? 1 : 0)); // entry (selectable)
            stream.Write((byte)(gs.Active ? 1 : 0)); // available
            if (gs.Active)
            {
                stream.Write((byte)gs.Load);  // con — overall congestion
                for (var i = 0; i < 10; i++)  // rcon — per-race congestion
                    stream.Write((byte)0);
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
        foreach (var character in characters)
        {
            stream.Write((ulong)character.AccountId); // accountId (uint64 on the wire)
            stream.Write(character.GsId);             // worldId
            stream.Write(character.Id);               // charId
            stream.Write(character.Name);             // name
            stream.Write(character.Race);             // CharRace
            stream.Write(character.Gender);           // CharGender
            stream.Write(new byte[16], true);         // guid (length-prefixed blob: [u16 16][16 bytes] — client reads it via the blob serializer)
            stream.Write(0UL);                        // v
        }

        return stream;
    }
}