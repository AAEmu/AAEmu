using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetOverHeadMarkerPacket : GamePacket
    {
        public CSSetOverHeadMarkerPacket() : base(0x086, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var markerIndex = stream.ReadInt32();

            uint id = 0;

            var type = stream.ReadByte();
            if (type == 1)
                id = stream.ReadUInt32();
            if (type == 2)
                id = stream.ReadBc();

            _log.Warn("SetOverHeadMarker, TeamId: {0}, MarkerIndex: {1}, Id: {2}", teamId, markerIndex, id);
        }
    }
}
