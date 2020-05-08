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
            stream.Write((short) _type);
            stream.Write((short) (_character?.Faction.Id ?? 0)); //chat
            stream.Write(_character?.Faction.Id ?? 0); //chat, factionId?
            stream.WriteBc(_character?.ObjId ?? 0);
            stream.Write(_character?.Id ?? 0);
            stream.Write(_character != null ? _languageType : (byte) 0);
            stream.Write(_character != null ? (byte) _character.Race : (byte) 0);
            stream.Write(_character?.Faction.Id ?? 0); //type, factionId?
            if (_character?.Connection?.GetAttribute("gmFlag") != null)
                stream.Write(_character != null ? "GM "+_character.Name : "");
            else
                stream.Write(_character != null ? _character.Name : "");
            stream.Write(_message);
            stream.Write(_character != null ? _ability : 0);
            stream.Write(0); //option
            return stream;
        }
    }
}
