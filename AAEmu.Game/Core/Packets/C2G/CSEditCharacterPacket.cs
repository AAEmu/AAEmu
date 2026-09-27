using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The lobby's ApplyEditCharacter. 10.0.2.13 body:
///   type        : u64   character id
///   name        : string, capacity 0x80
///   CharRace    : u8
///   CharGender  : u8
/// type x7     : i32   body items, face.. beard (equipment slots 19..25)
/// appearance  : the block CSCreateCharacter and the unit state share
///   ability x3  : u8
///   level       : u8
/// The same fields as CSCreateCharacter (it differs only by the leading id and the missing
/// introZoneId), so the reader is shared.
/// </summary>
public class CSEditCharacterPacket() : GamePacket(CSOffsets.CSEditCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var characterId = (uint)stream.ReadUInt64();
        var name = stream.ReadString();
        var race = (Race)stream.ReadByte();
        var gender = (Gender)stream.ReadByte();
        var items = new uint[CharacterEditRules.BodyItemCount];
        for (var i = 0; i < items.Length; i++)
            items[i] = stream.ReadUInt32();

        var customModel = new UnitCustomModelParams();
        customModel.Read(stream);

        var ability1 = (AbilityType)stream.ReadByte();
        var ability2 = (AbilityType)stream.ReadByte();
        var ability3 = (AbilityType)stream.ReadByte();
        var level = stream.ReadByte();

        CharacterManager.Instance.EditCharacter(Connection,
            new CharacterEditRequest(characterId, name, race, gender, items, customModel, ability1, ability2, ability3, level));
    }
}
