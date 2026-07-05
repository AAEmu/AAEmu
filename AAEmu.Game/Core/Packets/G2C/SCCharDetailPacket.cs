using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharDetailPacket(Character character, bool success) : GamePacket(SCOffsets.SCCharDetailPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.Id);
        stream.Write(character.Name);
        stream.Write((byte)character.Race);
        stream.Write(character.Hp * 100); // TODO: precise health ?
        stream.Write(character.Level);
        stream.Write((byte)character.Ability1);
        stream.Write((byte)character.Ability2);
        stream.Write((byte)character.Ability3);
        stream.Write(Helpers.ConvertLongX(character.Transform.World.Position.X));
        stream.Write(Helpers.ConvertLongY(character.Transform.World.Position.Y));
        stream.Write(character.Transform.World.Position.Z);
        stream.Write(character.Transform.ZoneId);
        stream.Write(character.LeaveTime); // TODO: lastWorldLeaveTime

        var items = character.Inventory.Equipment.GetSlottedItemsList();
        foreach (var item in items)
        {
            if (item == null)
                stream.Write(0);
            else
                stream.Write(item);
        }

        stream.Write(success);
        return stream;
    }
}
