using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadsCreatedPacket : GamePacket
    {
        private readonly Doodad[] _doodads;

        public SCDoodadsCreatedPacket(Doodad[] doodads) : base(0x110, 1) // 0x10c
        {
            _doodads = doodads;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _doodads.Length);
            foreach (var doodad in _doodads)
            {
                stream.WriteBc(doodad.ObjId);
                stream.Write(doodad.TemplateId);
                stream.WriteBc(doodad.OwnerBcId);
                stream.WriteBc(0);
                stream.Write((byte) 255); // attachPoint
                stream.Write(Helpers.ConvertX(doodad.Position.X));
                stream.Write(Helpers.ConvertY(doodad.Position.Y));
                stream.Write(Helpers.ConvertZ(doodad.Position.Z));
                stream.Write(Helpers.ConvertRotation(doodad.Position.RotationX));
                stream.Write(Helpers.ConvertRotation(doodad.Position.RotationY));
                stream.Write(Helpers.ConvertRotation(doodad.Position.RotationZ));
                stream.Write(doodad.Scale);
                stream.Write(false); // hasLootItem
                stream.Write(doodad.FuncGroupId); // doodad_func_groups Id
                stream.Write(doodad.OwnerId); // characterId
                stream.Write((long) 0); // type(id)
                stream.Write(doodad.ItemId); // item Id
                stream.Write(0u); // type(id)
                stream.Write(doodad.TimeLeft); // growing
                stream.Write(doodad.PlantTime);
                stream.Write(10u); // type(id)?
                stream.Write(0); // family
                stream.Write(-1); // puzzleGroup
                stream.Write((byte) doodad.OwnerType); // ownerType
                stream.Write(0u); // dbHouseId
                stream.Write(0); // data
            }

            return stream;
        }
    }
}
