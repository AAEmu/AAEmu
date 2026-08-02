using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharDetailPacket(Character character = null) : GamePacket(SCOffsets.SCCharDetailPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (character == null)
        {
            WriteEmptyDetail(stream);
            stream.Write(false);
            return stream;
        }

        var position = character.Transform.World.Position;
        stream.Write((ulong)character.Id);                       // charId (u64)
        stream.Write(character.Name ?? string.Empty);            // name
        stream.Write((byte)character.Race);                       // CharRace (u8)
        stream.Write((uint)Math.Max(character.Hp, 0));            // health (u32, not preciseHealth)
        stream.Write(character.Level);                            // level (u8)
        stream.Write((byte)character.Ability1);                   // ability1 (u8)
        stream.Write((byte)character.Ability2);                   // ability2 (u8)
        stream.Write((byte)character.Ability3);                   // ability3 (u8)
        stream.Write((ulong)Helpers.ConvertLongX(position.X));    // x (u64)
        stream.Write((ulong)Helpers.ConvertLongY(position.Y));    // y (u64)
        stream.Write(position.Z);                                 // z (f32)
        stream.Write((int)character.Transform.ZoneId);            // zoneId (i32)
        stream.Write(character.LeaveTime);                        // lastWorldLeaveTime (i64)
        EquipmentSerializer.Write(stream, character, BaseUnitType.Character);
        stream.Write(true);
        return stream;
    }

    private static void WriteEmptyDetail(PacketStream stream)
    {
        stream.Write(0UL);          // charId
        stream.Write(string.Empty); // name
        stream.Write((byte)0);      // CharRace
        stream.Write(0u);           // health
        stream.Write((byte)0);      // level
        stream.Write((byte)0);      // ability1
        stream.Write((byte)0);      // ability2
        stream.Write((byte)0);      // ability3
        stream.Write(0UL);          // x
        stream.Write(0UL);          // y
        stream.Write(0f);           // z
        stream.Write(0);            // zoneId
        stream.Write(0L);           // lastWorldLeaveTime
        stream.Write(0UL);          // equipment validFlags
        stream.Write(0UL);          // equipment trailing flags
    }
}
