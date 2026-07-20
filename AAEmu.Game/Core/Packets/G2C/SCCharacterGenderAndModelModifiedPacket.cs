using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterGenderAndModelModifiedPacket(Character character)
    : GamePacket(SCOffsets.SCCharacterGenderAndModelModifiedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.Id);
        stream.Write(character.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Hair)?.TemplateId ?? 0);
        character.ModelParams.Write(stream);
        stream.Write((uint)0); // I got no idea what this is, but it gives a packet error without it
        return stream;
    }
}
