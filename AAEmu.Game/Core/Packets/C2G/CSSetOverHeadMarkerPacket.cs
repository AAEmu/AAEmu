using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Team;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSetOverHeadMarkerPacket : GamePacket
    {
        public CSSetOverHeadMarkerPacket() : base(CSOffsets.CSSetOverHeadMarkerPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var teamId = stream.ReadUInt32();
            var index = (OverHeadMark)stream.ReadInt32();

            uint id = 0;

            var type = stream.ReadByte();
            if (type == 1)
                id = stream.ReadUInt32();
            if (type == 2)
                id = stream.ReadBc();

            // _log.Warn("SetOverHeadMarker, teamId: {0}, index: {1}, type: {2}, id: {3}", teamId, index, type, id);
            var owner = Connection.ActiveChar;
            if (teamId > 0)
            {
                TeamManager.Instance.SetOverHeadMarker(owner, teamId, index, type, id);
            }
            else
            {
                owner.SendPacket(new SCOverHeadMarkerSetPacket(0, index, type == 2, id));
            }
        }
    }
}
