using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateCharacterPacket : GamePacket
    {
        public CSCreateCharacterPacket() : base(CSOffsets.CSCreateCharacterPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString(); // name
            var race = stream.ReadByte();    // CharRace
            var gender = stream.ReadByte();  // CharGender
            var items = new uint[7];
            for (var i = 0; i < 7; i++) // ItemCount
            {
                items[i] = stream.ReadUInt32(); // type BodyItemId
            }

            var customModel = new UnitCustomModelParams(UnitCustomModelType.Face);
            customModel.Read(stream);

            var ability = new byte[3];
            for (var i = 0; i < 3; i++)
            {
                ability[i] = stream.ReadByte();
            }
            var level = stream.ReadByte();
            var IntroZoneId = stream.ReadInt32(); // for 3.0.3.0

            CharacterManager.Instance.Create(Connection, name, race, gender, items, customModel, ability, level, IntroZoneId);
        }
    }
}
