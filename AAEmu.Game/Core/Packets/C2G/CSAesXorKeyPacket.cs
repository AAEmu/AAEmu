using System.Linq;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client reply to the RSA key exchange started by X2EnterWorldResponse: carries the client's AES + XOR
/// session keys, each RSA-encrypted (128 bytes) with the server's public key. Sent at level 1 (plaintext),
/// since the C->S encryption it establishes is not active yet.
///
/// NOTE: name is the emulator's descriptive placeholder — the real key reply is an inner packet (opcode
/// 0x1AC) carried inside X2ClientToWorldPacket; the client exposes no distinct class name for it.
/// </summary>
public class CSAesXorKeyPacket() : GamePacket(CSOffsets.CSAesXorKeyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Recover the client's RSA-encrypted AES + XOR keys. Wrapped defensively so that even if the exact
        // blob layout differs from the assumed [int][short][128][128], the char-select push below still runs
        // (the S->C StoC cipher is keyless, so char-select renders without the C->S keys).
        try
        {
            _ = stream.ReadInt32();              // AES blob length
            _ = stream.ReadInt16();              // XOR blob length
            var encAes = stream.ReadBytes(128);  // RSA-encrypted AES key
            var encXor = stream.ReadBytes(128);  // RSA-encrypted XOR head
            EncryptionManager.Instance.StoreClientKeys(encAes, encXor, Connection.AccountId, Connection.Id);
        }
        catch (System.Exception)
        {
            // ignore — proceed to char-select regardless
        }

        // Key exchange done — push the lobby / character-select data (encrypted, level 5).
        // sc = creatable character-slot count. 0 made every slot show "캐릭터 생성 불가" (cannot create);
        // send the max (4) so creation is allowed. TODO: derive from account/server config.
        Connection.SendPacket(new SCGetSlotCountPacket(4));
        Connection.SendPacket(new SCAccountInfoPacket(
            (int)Connection.Payment.Method,
            Connection.Payment.Location,
            Connection.Payment.StartTime,
            Connection.Payment.EndTime));

        Connection.LoadAccount();

        // 10.0.2.13 lobby char struct is now implemented (Character.WriteLobby1013) — send the real list.
        var characters = Connection.Characters?.Values.ToArray() ?? System.Array.Empty<Character>();
        Connection.SendPacket(new SCRaceCongestionPacket());

        if (characters.Length == 0)
        {
            Connection.SendPacket(new SCCharacterListPacket(true, characters));
        }
        else
        {
            for (var i = 0; i < characters.Length; i += 2)
            {
                var last = characters.Length - i <= 2;
                var temp = new Character[last ? characters.Length - i : 2];
                System.Array.Copy(characters, i, temp, 0, temp.Length);
                Connection.SendPacket(new SCCharacterListPacket(last, temp));
            }
        }

        // Featured/representative character for the character-select screen. The reference sends
        // SCRepreSentCharacter (0x2C4) right after the character list; represent the first character
        // (success/first true), or an empty representation when the account has none.
        if (characters.Length > 0)
            Connection.SendPacket(new SCRepreSentCharacterPacket(characters[0].Id, true, true, false));
        else
            Connection.SendPacket(new SCRepreSentCharacterPacket(0, false, false, false));
    }
}
