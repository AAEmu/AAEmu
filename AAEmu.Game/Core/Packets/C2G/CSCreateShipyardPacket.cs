using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateShipyardPacket : GamePacket
    {
        public CSCreateShipyardPacket() : base(CSOffsets.CSCreateShipyardPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            var zRot = stream.ReadSingle();
            var designItem = stream.ReadUInt64();
            var mAABBmnX = stream.ReadSingle();
            var mAABBmnY = stream.ReadSingle();
            var mAABBmnZ = stream.ReadSingle();
            var mAABBmxX = stream.ReadSingle();
            var mAABBmxY = stream.ReadSingle();
            var mAABBmxZ = stream.ReadSingle();
            var autoUseAAPoint = stream.ReadBoolean();

            _log.Warn("CreateShipyard, Id: {0}, X: {1}, Y: {2}, Z: {3}, DesignItem: {4}", id, x, y, z, designItem);

            ShipyardManager.Instance.Create(Connection.ActiveChar, id, x, y, z, (short)zRot, 0, 0, 0, 0);
        }
    }
}
