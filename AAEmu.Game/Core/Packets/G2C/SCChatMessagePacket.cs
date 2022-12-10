using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatMessagePacket : GamePacket
    {
        private readonly ChatType _type;
        private readonly Character _character;
        private readonly string _message;
        private readonly int _ability;
        private readonly byte _languageType;
        private readonly byte[] _linkType;
        private readonly ushort[] _start;
        private readonly ushort[] _lenght;
        private readonly Dictionary<int, byte[]> _data;
        private readonly uint[] _qType;
        private readonly ulong[] _itemId;

        public SCChatMessagePacket(ChatType type, string message, byte[] linkType = null) :
            base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = type;
            _message = message;
            _linkType = linkType;
        }

        public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType, byte[] linkType = null) :
            base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = type;
            _character = character;
            _message = message;
            _ability = ability;
            _languageType = languageType;
            _linkType = linkType;
        }

        public SCChatMessagePacket(
            ChatType type, Character character, string message, int ability, byte languageType, byte[] linkType, ushort[] start, ushort[] lenght, Dictionary<int, byte[]> data, uint[] qType, ulong[] itemId) :
            base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = type;
            _character = character;
            _message = message;
            _ability = ability;
            _languageType = languageType;
            _linkType = linkType;
            _start = start;
            _lenght = lenght;
            _data = data;
            _qType = qType;
            _itemId = itemId;
        }

        public SCChatMessagePacket(ChatType type, string message) : base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = type;
            _message = message;
        }

        public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType) :
            base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = type;
            _character = character;
            _message = message;
            _ability = ability;
            _languageType = languageType;
        }

        public override PacketStream Write(PacketStream stream)
        {
            #region Int64_chat
            stream.Write((short)_type);                         // ChatType -> ChatChannelNo
            stream.Write((short)(_character?.Faction.Id ?? 0)); // chat, subType
            stream.Write(_character?.Faction.Id ?? 0);           // chat, factionId
            #endregion Int64_chat

            stream.WriteBc(_character?.ObjId ?? 0);
            stream.Write(_character?.Id ?? 0);
            stream.Write(_character != null ? _languageType : (byte)0);
            stream.Write(_character != null ? (byte)_character.Race : (byte)0);
            stream.Write(_character?.Faction.Id ?? 0); //type
            stream.Write(_character != null ? _character.Name : "");

            stream.Write(_message);

            for (var i = 0; i < 4; i++)
            {
                var linkedType = _linkType?[i] ?? 0;
                stream.Write(linkedType); // linkType

                if (linkedType > 0)
                {
                    stream.Write(_start[i]);
                    stream.Write(_lenght[i]);
                    switch (linkedType)
                    {
                        case 1:
                            stream.Write(_data[i]);   // data length = 208
                            break;
                        case 3:
                            stream.Write(_qType[i]);  // qType
                            break;
                        case 4:
                            stream.Write(_itemId[i]); // itemId
                            break;
                    }
                }
            }

            stream.Write(_character != null ? _ability : 0);
            stream.Write((byte)0); //option int in 1.2, byte in 3+
            return stream;
        }
    }
}
