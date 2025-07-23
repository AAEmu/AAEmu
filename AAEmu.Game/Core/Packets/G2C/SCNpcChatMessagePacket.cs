using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNpcChatMessagePacket : GamePacket
{
    private readonly ChatType _chatType;
    private readonly Npc _npc;
    private readonly Character _character;
    private readonly uint _type;
    private readonly byte _kind;
    private readonly string _message;

    public SCNpcChatMessagePacket(ChatType chatType, Npc npc, Character character, byte kind, uint type, string message)
        : base(SCOffsets.SCNpcChatMessagePacket, 1)
    {
        _chatType = chatType;
        _npc = npc;
        _character = character;
        _kind = kind;
        _type = type;
        _message = message;
    }

    public override PacketStream Write(PacketStream stream)
    {
        #region Int64_chat
        stream.Write((short)_chatType);                     // ChatType -> ChatChannelNo
        stream.Write((short)(_character?.Faction.Id ?? 0)); // chat, subType
        stream.Write((uint)(_character?.Faction.Id ?? 0));  // chat, factionId
        #endregion Int64_chat

        stream.WriteBc(_npc.ObjId);             // bc
        stream.Write(_npc.Name);                // name
        stream.WriteBc(_character?.ObjId ?? 0); // bc
        stream.Write(_kind);                    // kind
        if (_kind == 1)
            stream.Write(_type);
        else
            stream.Write(_message);

        return stream;
    }
}
