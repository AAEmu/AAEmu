using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRotateHousePacket : GamePacket
    {
        public CSRotateHousePacket() : base(CSOffsets.CSRotateHousePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var zRot = stream.ReadSingle();
            var height = stream.ReadSingle();

            _log.Debug("CSRotateHousePacket, objId: {0}, zRot: {1}, height: {2}", objId, zRot, height);
        }
    }
}
