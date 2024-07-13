using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSGetResidentDescPacket : GamePacket
    {
        public CSGetResidentDescPacket() : base(CSOffsets.CSGetResidentDescPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var zoneGroupId = stream.ReadUInt16();
            var unk = stream.ReadUInt32();

            Logger.Debug("CSGetResidentDescPacket");

            ResidentManager.Instance.UpdateResidenMemberInfo(zoneGroupId, Connection.ActiveChar);
        }
    }
}
