using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestPermissionToPlayCinemaForDirectingMode : GamePacket
    {
        public CSRequestPermissionToPlayCinemaForDirectingMode() : base(CSOffsets.CSRequestPermissionToPlayCinemaForDirectingMode, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var questContextId = stream.ReadUInt32();
            var npcObjId = stream.ReadBc();
            var doodadObjId = stream.ReadBc();

            _log.Warn("CSRequestPermissionToPlayCinemaForDirectingMode");
        }
    }
}
