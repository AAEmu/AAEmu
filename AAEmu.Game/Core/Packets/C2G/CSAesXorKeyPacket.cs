using System.Linq;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Client reply to the RSA key exchange started by X2EnterWorldResponse: carries the client's AES + XOR
/// session keys, each RSA-encrypted (128 bytes) with the server's public key. Sent at level 1 (plaintext),
/// since the C->S encryption it establishes is not active yet.
/// On return to character select, the existing encrypted session reuses this packet as a lobby
/// re-entry cue, so the server republishes the character list without re-keying.
/// </summary>
public class CSAesXorKeyPacket() : GamePacket(CSOffsets.CSAesXorKeyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // First enter uses a plaintext RSA reply; lobby re-entry uses the existing encrypted session.
        if (Level != 1)
        {
            if (Connection.EncryptionActive && Connection.State == GameState.Lobby)
            {
                Logger.Info(
                    "CSAesXorKey at L{0} during lobby re-entry (acc={1}) — publishing character list without re-key",
                    Level, Connection.AccountId);
                PublishCharacterSelect();
                return;
            }

            Logger.Warn("Ignoring CSAesXorKeyPacket on transport level {0}; the key reply must be level 1", Level);
            return;
        }

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

        PublishCharacterSelect();
    }

    /// <summary>
    /// Lobby payloads that unlock character select (slots + list + featured portrait).
    /// Shared by first-enter RSA completion and in-session return-from-world.
    /// </summary>
    private void PublishCharacterSelect()
    {
        // Key exchange done — push the lobby / character-select data (encrypted, level 5).
        // sc = creatable character-slot count. 0 made every slot show "캐릭터 생성 불가" (cannot create);
        Connection.SendPacket(new SCGetSlotCountPacket(
            AppConfiguration.Instance.InitialConfig.MaxCharacterSlots));
        Connection.SendPacket(new SCAccountInfoPacket(
            (int)Connection.Payment.Method,
            Connection.Payment.Location,
            Connection.Payment.StartTime,
            Connection.Payment.EndTime,
            Connection.Payment.RealPayTimeSeconds,
            Connection.Payment.BuyPremiumCount));

        // The client's membership flags do not survive the trip back from in-world, so returning to
        // character select showed "Patron 0" again until this went out a second time.
        AccountAttributePublisher.Send(Connection);

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

        // The account's main ("represent") character for the character-select screen, sent right after
        // the character list.
        //
        // This used to name characters[0] unconditionally with success=true. The client's handler for
        // 0x2C4 stores that id as THE represent character (RVA 0x4DD270), and its delete dialog then
        // refuses the first character on every account with "Must deselect as Main Character before
        // deleting." - a main character nobody had chosen, reasserted at every login. Send whoever the
        // player actually nominated instead, and success=false when that is nobody, which leaves the
        // client's id at its default of zero.
        var represent = characters.FirstOrDefault(c => c.IsRepresent);
        if (represent != null)
            Connection.SendPacket(new SCRepreSentCharacterPacket(represent.Id, true, true, false));
        else
            Connection.SendPacket(new SCRepreSentCharacterPacket(0, false, true, false));
    }
}
