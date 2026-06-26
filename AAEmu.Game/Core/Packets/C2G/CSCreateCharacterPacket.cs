using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateCharacterPacket() : GamePacket(CSOffsets.CSCreateCharacterPacket, 1)
{
    // 10.0.2.13 CSCreateCharacter (opcode 0x049) body. Layout reverse-engineered from the client
    // serializer CreateCharacterPacket vtable[2] = sub_39ABB1E0:
    //   name        : string
    //   CharRace    : u8   (NOTE: client serializes race/gender/ability/level as 1 byte, not u32)
    //   CharGender  : u8
    //   bodyItems   : u32 x7   ("type")
    //   appearance  : sub_39A3E0F0 (variable-length CustomModel: ext flag, face, hair, decals, sliders)
    //   ability1..3 : u8 x3
    //   level       : u8
    //   introZoneId : u32
    // The appearance block is variable-length and not yet fully modeled, so it is read as a raw blob:
    // everything between the 7 body items and the fixed 8-byte tail (3*ability + level + 4*introZoneId).
    public override void Read(PacketStream stream)
    {
        var start = stream.Pos;
        var raw = stream.ReadBytes();
        Logger.Warn("CSCreateCharacter RAW ({0}b): {1}", raw.Length, Convert.ToHexString(raw));
        stream.Pos = start;

        var name = stream.ReadString();
        var race = (Race)stream.ReadByte();
        var gender = (Gender)stream.ReadByte();
        var items = new uint[7];
        for (var i = 0; i < 7; i++)
            items[i] = stream.ReadUInt32();

        // appearance = remaining bytes minus the fixed 8-byte tail (ability1,2,3 + level + introZoneId u32)
        var appearanceLen = stream.LeftBytes - 8;
        Logger.Warn("CSCreateCharacter name={0} race={1} gender={2} items=[{3}] appearanceLen={4} left={5}",
            name, race, gender, string.Join(",", items), appearanceLen, stream.LeftBytes);
        var appearance = appearanceLen > 0 ? stream.ReadBytes(appearanceLen) : Array.Empty<byte>();

        var ability1 = (AbilityType)stream.ReadByte();
        var ability2 = (AbilityType)stream.ReadByte();
        var ability3 = (AbilityType)stream.ReadByte();
        var level = stream.ReadByte();
        var introZoneId = stream.ReadUInt32();
        Logger.Warn("CSCreateCharacter ability=[{0},{1},{2}] level={3} introZoneId={4} appHex={5}",
            (byte)ability1, (byte)ability2, (byte)ability3, level, introZoneId,
            appearance.Length <= 64 ? Convert.ToHexString(appearance) : Convert.ToHexString(appearance)[..128] + "...");

        // TODO: decode the appearance blob into UnitCustomModelParams (sub_39A3E0F0) for proper round-trip.
        var customModel = new UnitCustomModelParams();

        CharacterManager.Instance.Create(Connection, name, race, gender, items, customModel, ability1, ability2, ability3, level);
    }
}
