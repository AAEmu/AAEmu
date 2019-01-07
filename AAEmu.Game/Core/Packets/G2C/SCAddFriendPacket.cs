using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAddFriendPacket : GamePacket
    {
        private readonly Character _character;
        private readonly bool _success;
        private readonly short _errorMessage;

        public SCAddFriendPacket(Character character, bool success, short errorMessage) : base(0x04b, 1)
        {
            _character = character;
            _success = success;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_character.Id);
            stream.Write(_character.Name);
            stream.Write((byte) _character.Race);
            stream.Write(_character.Level);
            stream.Write(_character.Hp);
            stream.Write((byte) _character.Ability1);
            stream.Write((byte) _character.Ability2);
            stream.Write((byte) _character.Ability3);
            stream.Write(Helpers.ConvertLongX(_character.Position.X));
            stream.Write(Helpers.ConvertLongY(_character.Position.Y));
            stream.Write(_character.Position.Z);
            stream.Write(_character.Position.ZoneId);
            stream.Write(0u); // type(id)
            stream.Write(false); // isParty
            stream.Write(true); // isOnline
            stream.Write(DateTime.Now); // lastWorldLeaveTime

            stream.Write(_success);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}