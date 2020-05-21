using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharDetailPacket : GamePacket
    {
        private readonly Character _character;
        private readonly bool _success;
        
        public SCCharDetailPacket(Character character, bool success) : base(SCOffsets.SCCharDetailPacket, 1)
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
            stream.Write(Helpers.ConvertLongX(_character.Position.X));
            stream.Write(Helpers.ConvertLongY(_character.Position.Y));
            stream.Write(_character.Position.Z);
            stream.Write(_character.Position.ZoneId);
            stream.Write(DateTime.Now); // lastWorldLeaveTime

            var items = _character.Inventory.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item == null)
                    stream.Write(0);
                else
                    stream.Write(item);
            }

            stream.Write(_success);
            return stream;
        }
    }
}
