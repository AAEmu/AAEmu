using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateHousePacket : GamePacket
    {
        public CSCreateHousePacket() : base(0x055, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var designId = stream.ReadUInt32();
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            var zRot = stream.ReadSingle();
            var itemId = stream.ReadUInt64();
            var moneyAmount = stream.ReadInt32();
            var ht = stream.ReadInt32();
            var autoUseAaPoint = stream.ReadBoolean();

            _log.Debug("CreateHouse, Id: {0}, X: {1}, Y: {2}, Z: {3}, ZRot: {4}", designId, x, y, z, zRot);

            var zoneId = WorldManager.Instance.GetZoneId(Connection.ActiveChar.InstanceId, x, y);
            var house = HousingManager.Instance.Create(designId);
            house.Position = new Point(zoneId, x, y, z);

            house.Spawn();
        }
    }
}
