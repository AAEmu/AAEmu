using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRefreshResidentMembersPacket : GamePacket
    {
        public CSRefreshResidentMembersPacket() : base(CSOffsets.CSRefreshResidentMembersPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var zoneGroupId = stream.ReadUInt16();

            Logger.Debug("CSRefreshResidentMembersPacket");

            ResidentManager.Instance.UpdateResidenMemberInfo(zoneGroupId, Connection.ActiveChar);
        }
    }
}
