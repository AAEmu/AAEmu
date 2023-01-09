using System;
using System.Reactive;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharDetailPacket : GamePacket
    {
        private readonly Character _character;
        private readonly bool _success;
        
        public SCCharDetailPacket(Character character, bool success) : base(SCOffsets.SCCharDetailPacket, 5)
        {
            _character = character;
            _success = success;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_character.Id);
            stream.Write(_character.Name);
            stream.Write((byte)_character.Race);
            stream.Write(_character.Hp * 100); // health TODO ?
            stream.Write(_character.Level);
            stream.Write((byte)_character.Ability1);
            stream.Write((byte)_character.Ability2);
            stream.Write((byte)_character.Ability3);
            stream.Write(Helpers.ConvertLongX(_character.Transform.Local.Position.X));
            stream.Write(Helpers.ConvertLongY(_character.Transform.Local.Position.Y));
            stream.Write(_character.Transform.Local.Position.Z);
            stream.Write(_character.Transform.ZoneId);
            stream.Write(_character.LeaveTime); // lastWorldLeaveTime

            #region CharacterInfo_3EB0

            _character.Inventory_Equip(stream);

            #endregion CharacterInfo_3EB0

            stream.Write(_success);

            return stream;
        }
    }
}
