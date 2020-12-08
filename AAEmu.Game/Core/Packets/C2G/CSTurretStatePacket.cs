using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTurretStatePacket : GamePacket
    {
        public CSTurretStatePacket() : base(CSOffsets.CSTurretStatePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var unitId = stream.ReadBc();
            var pitch = stream.ReadSingle();
            var yaw = stream.ReadSingle();

            _log.Debug("TurretState, UnitId: {0}, Pitch: {1}, Yaw: {2}", unitId, pitch, yaw);
        }
    }
}
