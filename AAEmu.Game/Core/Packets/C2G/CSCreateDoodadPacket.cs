using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateDoodadPacket : GamePacket
    {
        public CSCreateDoodadPacket() : base(0x0e6, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            var zRot = stream.ReadSingle();
            var scale = stream.ReadSingle();
            var itemId = stream.ReadUInt64();

            _log.Warn("CreateDoodad, Id: {0}, X: {1}, Y: {2}, Z: {3}, ItemId: {4}", id, x, y, z, itemId);


            var doodadSpawner = new DoodadSpawner();
            doodadSpawner.Id = 0;
            doodadSpawner.UnitId = id;
            doodadSpawner.Position = Connection.ActiveChar.Position.Clone();
            doodadSpawner.Position.X = x;
            doodadSpawner.Position.Y = y;
            doodadSpawner.Position.Z = z;
            doodadSpawner.Position.RotationX = 0;
            doodadSpawner.Position.RotationY = 0;
            doodadSpawner.Position.RotationZ = 0;
            doodadSpawner.Scale = scale;
            var doodad = doodadSpawner.Spawn(0);
            if (doodad == null)
            {
                _log.Warn("Doodad {0}, from spawn not exist at db", id);
            }
            else
            {
                doodad.OwnerType = DoodadOwnerType.Character;
                doodad.OwnerId = Connection.ActiveChar.Id;
                doodad.OwnerObjId = Connection.ActiveChar.ObjId;
                //doodad.Position = new Models.Game.World.Point(Connection.ActiveChar.Position.WorldId, Connection.ActiveChar.Position.ZoneId, x, y, z, 0, 0, 0);
                if (scale > 0)
                    doodad.SetScale(scale);
                doodad.Spawn();
            }

        }
    }
}
