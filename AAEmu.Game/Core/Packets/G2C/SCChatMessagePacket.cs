using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C;

#pragma warning disable IDE0052 // Remove unread private members

public class SCChatMessagePacket : GamePacket
{
    private readonly ChatType _type;
    private readonly Character _character;
    private readonly string _message;
    private readonly int _ability;
    private readonly byte _languageType;

    public SCChatMessagePacket(ChatType type, string message) : base(SCOffsets.SCChatMessagePacket, 1)
    {
        _type = type;
        _message = message;
    }

    public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType) :
        base(SCOffsets.SCChatMessagePacket, 1)
    {
        _type = type;
        _character = character;
        _message = message;
        _ability = ability;
        _languageType = languageType;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Wire layout validated against CN 10.0.2.13 live sniff (SCChatMessage 0x102):
        //   26-byte header + name + msg + 4×linkType(u8) + ability(i32) + 3-byte trailer.
        // Truncating this body caused "not enough buffer for option/worldId" → sc desync → DC.
        WriteChatHeader(stream);
        stream.WriteBc(_character?.ObjId ?? 0);
        stream.Write(_character?.Id ?? 0);
        stream.Write(_character != null ? _languageType : (byte)0);
        stream.Write(_character != null ? (byte)_character.Race : (byte)0);

        // 9 bytes after race (sniff): u32 + u32 + u8. System MOTD is all zero; player chat
        // carries faction-like values in this block.
        var faction = (uint)(_character?.Faction.Id ?? 0);
        stream.Write(faction);
        stream.Write(faction);
        stream.Write((byte)0);

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

    private void WriteChatHeader(PacketStream stream)
    {
        // System MOTD sniff starts FF FE FF 00 (not FE FF from (short)ChatType.System=-2).
        if (_type == ChatType.System && _character == null)
        {
            stream.Write((byte)0xFF);
            stream.Write((byte)0xFE);
            stream.Write((byte)0xFF);
            stream.Write((byte)0x00);
            stream.Write(0u); // pad to 8-byte chat block
            return;
        }

        stream.Write((short)_type);
        stream.Write((short)(_character?.Faction.Id ?? 0));
        stream.Write((uint)(_character?.Faction.Id ?? 0));
    }
}
