using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateCharacterPacket : GamePacket
{
    public CSCreateCharacterPacket() : base(CSOffsets.CSCreateCharacterPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var name = stream.ReadString();
        var race = (Race)stream.ReadByte();
        var gender = (Gender)stream.ReadByte();
        var items = new uint[7];
        for (var i = 0; i < 7; i++)
            items[i] = stream.ReadUInt32();

        var customModel = new UnitCustomModelParams();
        customModel.Read(stream);

        var ability1 = (AbilityType)stream.ReadByte();
        var ability2 = (AbilityType)stream.ReadByte();
        var ability3 = (AbilityType)stream.ReadByte();
        var level = stream.ReadByte();
        var IntroZoneId = stream.ReadInt32(); // for 3.0.3.0

        CharacterManager.Instance.Create(Connection, name, race, gender, items, customModel, ability1, ability2, ability3, level);
    }
}
