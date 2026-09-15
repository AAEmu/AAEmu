using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReportCrimePacket() : GamePacket(CSOffsets.CSReportCrimePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // The three value fields are the client's copy of the evidence doodad's own row: the loot
        // func's skill id, its next phase and the func row id - taken from the report UI's local
        // data. Live sample for a large bloodstain: 11672 / 1043 / 1840. The server reads its own
        // copy of those values when it resolves the report, so these are recorded, not trusted.
        var objId = stream.ReadBc();
        var skillId = stream.ReadUInt32();
        var nextPhase = stream.ReadUInt32();
        var funcId = stream.ReadUInt32();
        var msg = stream.ReadString();

        var reporter = Connection.ActiveChar;

        var bloodStainDoodad = reporter.ParentWorld?.GetDoodad(objId);
        if (bloodStainDoodad != null)
        {
            var criminalName = NameManager.Instance.GetCharacterName(bloodStainDoodad.OwnerId) ?? string.Empty;
            var crimeEvent = CrimeManager.Instance.ReportCrime(reporter, bloodStainDoodad, skillId, nextPhase, funcId, msg);
            if (crimeEvent != null)
            {
                Logger.Debug($"ReportCrime, ObjId: {objId}, Msg: {msg}, skill {skillId}, nextPhase {nextPhase}, func {funcId}. " +
                             $"Owner {criminalName} ({bloodStainDoodad.OwnerId}), OwnerDbId {bloodStainDoodad.OwnerDbId}");
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
