using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

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
        stream.Write(_character.Hp * 100); // TODO: precise health ?
        stream.Write(_character.Level);
        stream.Write((byte)_character.Ability1);
        stream.Write((byte)_character.Ability2);
        stream.Write((byte)_character.Ability3);
        stream.Write(Helpers.ConvertLongX(_character.Transform.Local.Position.X));
        stream.Write(Helpers.ConvertLongY(_character.Transform.Local.Position.Y));
        stream.Write(_character.Transform.Local.Position.Z);
        stream.Write(_character.Transform.ZoneId);
        stream.Write(DateTime.UtcNow); // TODO: lastWorldLeaveTime

        _character.Inventory.WriteInventoryEquip(stream, _character);

        stream.Write(_success);
        return stream;
    }
}
