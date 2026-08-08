using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

#pragma warning disable IDE0052 // Remove unread private members

public class SCChatMessagePacket : GamePacket
{
    private readonly ChatType _type;
    private readonly Character _character;
    private readonly string _message;
    private readonly int _ability;
    private readonly byte _languageType;

    /// <summary>Channel discriminators, mirroring what SCJoinedChatChannel announced for this channel.</summary>
    private readonly short _subType;
    private readonly FactionsEnum _faction;

    public SCChatMessagePacket(ChatType type, string message) : base(SCOffsets.SCChatMessagePacket, 1)
    {
        _type = type;
        _message = message;
    }

    public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType,
        short subType = 0, FactionsEnum? faction = null) :
        base(SCOffsets.SCChatMessagePacket, 1)
    {
        _type = type;
        _character = character;
        _message = message;
        _ability = ability;
        _languageType = languageType;
        _subType = subType;
        // Deliberately NOT the character's faction. Only a real ChatChannel knows the discriminators
        // it announced in SCJoinedChatChannel; a direct broadcast (say, whisper) has none, and filling
        // them in from the sender names a channel the client never joined - which silently swallowed
        // those messages until this defaulted back to zero.
        _faction = faction ?? 0;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Header field order read out of the client's own serializer, which names every value
        // (SCChatMessage::Read, VA 0x39C71C30):
        //
        //   cliLocale     u8    1     off  0
        //   chat          u64   8     off  1
        //   bc            raw   3     off  9
        //   type          i64   8     off 12
        //   LanguageType  u8    1     off 20
        //   CharRace      u8    1     off 21
        //   type          u32   4     off 22
        //                      ---
        //                       26
        //
        // Widths come from the archive's vtable thunks: +0x90 reads 1, +0x88 reads 2, +0x80 and
        // +0xA0 read 4, +0x98 reads 8, +0x1A0 reads the 3-byte compressed id, and +0x1A8/+0x1D0
        // consume nothing at all (they are `ret` stubs on the read side).
        //
        // The previous layout was reconstructed from a byte sniff and got the SIZE right - also 26 -
        // but split it as i16+i16+u32 | bc | u32 | u8 | u8 | u32+u32+u8. Everything therefore sat one
        // to five bytes off: bc at 8 instead of 9, LanguageType at 15 instead of 20, CharRace at 16
        // instead of 21. name and msg still began at 26, which is why nothing desynced and no message
        // was ever truncated - but the client read `chat` and `type` out of the wrong bytes, could not
        // resolve either to a real channel, and dropped every message it was handed.
        //
        // CSSendChatMessage already carries this correction on the receiving side, including the
        // detail that ChatType lives in the low 16 bits of the 8-byte `chat` field; this mirrors it.
        // `chat` is the same 8-byte channel descriptor SCJoinedChatChannel announces:
        // type (i16) | subType (i16) | factionId (u32). CSSendChatMessage reads it back the same way,
        // taking the ChatType out of the low 16 bits. Sending only the type - subType and faction left
        // at zero - names a channel the client never joined, so it drops the message for every channel
        // that is scoped by zone, party or faction. Say and whisper survive it because theirs are zero.
        var chatType = (short)_type;
        var faction = (uint)_faction;
        var chat = (ulong)(ushort)chatType
                   | ((ulong)(ushort)_subType << 16)
                   | ((ulong)faction << 32);

        stream.Write((byte)0);                    // cliLocale - server-side messages carry no client locale
        stream.Write(chat);                       // chat
        stream.WriteBc(_character?.ObjId ?? 0);   // bc
        stream.Write((long)chatType);             // type
        stream.Write(_character != null ? _languageType : (byte)0);
        stream.Write(_character != null ? (byte)_character.Race : (byte)0);
        stream.Write(faction);

        if (_character?.Connection?.GetAttribute("gmFlag") != null)
            stream.Write("GM " + _character.Name);
        else
            stream.Write(_character != null ? _character.Name : "");
        stream.Write(_message ?? "");

        // Fixed 4 chat-link slots (linkType 0 = empty).
        for (var i = 0; i < 4; i++)
            stream.Write((byte)0);

        stream.Write(_character != null ? _ability : 0);
        // Trailer is 3 bytes on 10.0.2.13 (not a single option i32/u8).
        // System sniff ends with 00 FF 00; player chat with 00 02 01 — use system form for
        // non-character messages, zeros+safe defaults otherwise.
        if (_character == null || _type == ChatType.System)
        {
            stream.Write((byte)0);
            stream.Write((byte)0xFF);
            stream.Write((byte)0);
        }
        else
        {
            stream.Write((byte)0);
            stream.Write((byte)0);
            stream.Write((byte)0);
        }

        return stream;
    }

}
