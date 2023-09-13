using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSReportCrimePacket : GamePacket
    {
        public CSReportCrimePacket() : base(CSOffsets.CSReportCrimePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO find what the unknowns are
            var objId = stream.ReadBc();
            var unkId = stream.ReadUInt32();
            var unk2Id = stream.ReadUInt32();
            var unk3Id = stream.ReadUInt32();
            var msg = stream.ReadString();

            _log.Warn("ReportCrime, ObjId: {0}, Msg: {1}, Id: {2}, {3}, {4}", objId, msg, unkId, unk2Id, unk3Id);
        }
    }
}
