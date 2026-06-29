using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateCharacterPacket() : GamePacket(CSOffsets.CSCreateCharacterPacket, 1)
{
    // 10.0.2.13 CSCreateCharacter (opcode 0x049) body. Layout from the dedicated-server serializer
    // CreateCharacterPacket::SerializeBody (x2game-dev_dedicate sub_39C3E1C0):
    //   name        : string
    //   CharRace    : u8
    //   CharGender  : u8
    //   bodyItems   : u32 x7   ("type")
    //   appearance  : LobbyChar_WriteAppearance 0x393881E0 (ext-gated variable block — UnitCustomModelParams)
    //   ability1..3 : u8 x3
    //   level       : u8
    //   introZoneId : u32
    public override void Read(PacketStream stream)
    {
        var name = stream.ReadString();
        var race = (Race)stream.ReadByte();
        var gender = (Gender)stream.ReadByte();
        var items = new uint[7];
        for (var i = 0; i < 7; i++)
            items[i] = stream.ReadUInt32();

        var customModel = new UnitCustomModelParams();
        customModel.Read(stream); // ext-gated appearance (same serializer as the unit-state/lobby block)

        var ability1 = (AbilityType)stream.ReadByte();
        var ability2 = (AbilityType)stream.ReadByte();
        var ability3 = (AbilityType)stream.ReadByte();
        var level = stream.ReadByte();
        _ = stream.ReadUInt32(); // introZoneId

        CharacterManager.Instance.Create(Connection, name, race, gender, items, customModel, ability1, ability2, ability3, level);
    }
}
