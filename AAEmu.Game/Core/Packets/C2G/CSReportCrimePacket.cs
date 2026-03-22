using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReportCrimePacket() : GamePacket(CSOffsets.CSReportCrimePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // TODO find what the unknowns are
        var objId = stream.ReadBc();
        var unkId = stream.ReadUInt32();
        var unk2Id = stream.ReadUInt32();
        var unk3Id = stream.ReadUInt32();
        var msg = stream.ReadString();

        var reporter = Connection.ActiveChar;

        var bloodStainDoodad = reporter.ParentWorld?.GetDoodad(objId);
        if (bloodStainDoodad != null)
        {
            var criminalName = NameManager.Instance.GetCharacterName(bloodStainDoodad.OwnerId) ?? string.Empty;
            var victimName = NameManager.Instance.GetCharacterName((uint)bloodStainDoodad.Data) ?? "Unknown person";
            var crimeEvent = CrimeManager.Instance.ReportCrime(Connection.ActiveChar, bloodStainDoodad, unkId, unk2Id, unk3Id, msg);
            if (crimeEvent != null)
            {
                Logger.Debug($"ReportCrime, ObjId: {objId}, Msg: {msg}, Id: {unkId} (0x{unkId:x8}), {unk2Id} (0x{unk2Id:X8}), {unk3Id} (0x{unk3Id:X8}). Owner {criminalName} ({bloodStainDoodad.OwnerId}), OwnerDbId {bloodStainDoodad.OwnerDbId}");
            }
            else
            {
                Logger.Warn($"ReportCrime, Report failed ObjId: {objId}, Msg: {msg}");
            }
        }
        else
        {
            Logger.Warn($"ReportCrime, Invalid evidence ObjId: {objId}, Msg: {msg}");
        }
    }
}
