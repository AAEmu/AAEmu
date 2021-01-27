using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatMessagePacket : GamePacket
    {
        private readonly short _type;
        private readonly Character _character;
        private readonly string _message;
        private readonly int _ability;
        private readonly byte _languageType;
        private readonly byte[] _linkType;
        private readonly short[] _start;
        private readonly short[] _lenght;
        private readonly Dictionary<int, byte[]> _data;

        public SCChatMessagePacket(ChatType type, string message, byte[] linkType = null) : base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = (short)type;
            _message = message;
            _linkType = linkType;
        }

        public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType, byte[] linkType = null) :
            base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = (short)type;
            _character = character;
            _message = message;
            _ability = ability;
            _languageType = languageType;
            _linkType = linkType;
        }

        public SCChatMessagePacket(ChatType type, Character character, string message, int ability, byte languageType, byte[] linkType,
            short[] start, short[] lenght, Dictionary<int, byte[]> data) :
            base(SCOffsets.SCChatMessagePacket, 5)
        {
            _type = (short)type;
            _character = character;
            _message = message;
            _ability = ability;
            _languageType = languageType;
            _linkType = linkType;
            _start = start;
            _lenght = lenght;
            _data = data;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_type);                                // ChatChannelNo
            stream.Write((short)(_character?.Faction.Id ?? 0)); //chat, subType
            stream.Write(_character?.Faction.Id ?? 0);          //chat, factionId

            stream.WriteBc(_character?.ObjId ?? 0);
            stream.Write(_character?.Id ?? 0);
            stream.Write(_character != null ? _languageType : (byte)0);
            stream.Write(_character != null ? (byte)_character.Race : (byte)0);
            stream.Write(_character?.Faction.Id ?? 0); //type
            stream.Write(_character != null ? _character.Name : "");
            stream.Write(_message);

            for (var i = 0; i < 4; i++) // in 1.2 & 3.5 = 4  link_Type
            {
                var start = _start?[i] ?? (short)0;
                stream.Write(start); // start

                var lenght = _lenght?[i] ?? (short)0;
                stream.Write(lenght); // length

                var linkedType = _linkType?[i] ?? (byte)0;
                stream.Write(linkedType); // linkType

                if (linkedType > 0)
                {
                    //var result = 1;
                    //var v2 = 0;
                    //var v4 = 0;
                    //var v5 = 0;
                    //var v6 = 0;

                    //if (result == 1)
                    //{
                    //    v4 = (v2 + 8);
                    //    v5 = 0;
                    //}
                    //else
                    //{
                    //    if (result != 3)
                    //    {
                    //        //return result;
                    //    }
                    //    v4 = (v2 + 8);
                    //    v5 = 0;
                    //}
                    //if (v5 == 0)
                    //{
                    //    //result = (v6->Bytes2)("data", v4);
                    //}
                    //else
                    //{
                    //    //result = (v6->Bytes)("data", v4);
                    //}

                    switch (linkedType)
                    {
                        case 1:
                        case 3:
                            stream.Write(_data[i]); // data length = 208
                            break;
                        case 4:
                            break;
                    }
                }
            }

            stream.Write(_ability); // ability Int32
            stream.Write(0);        // option Int32
            return stream;
        }
    }
}
