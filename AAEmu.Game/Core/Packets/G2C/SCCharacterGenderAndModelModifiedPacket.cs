using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterGenderAndModelModifiedPacket : GamePacket
    {
        private readonly Character _character;

        public SCCharacterGenderAndModelModifiedPacket(Character character) : base(SCOffsets.SCCharacterGenderAndModelModifiedPacket, 1)
        {
            _character = character;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_character.Id);
            stream.Write(_character.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Hair)?.TemplateId ?? (uint)0);
            _character.ModelParams.Write(stream);
            stream.Write((uint)0); // I got no idea what this is, but it gives a packet error without it
            return stream;
        }
    }
}
