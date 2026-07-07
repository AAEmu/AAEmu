using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReportCrimePacket() : GamePacket(CSOffsets.CSReportCrimePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var skillId = stream.ReadUInt32();
        var doodadNextFuncGroup = stream.ReadInt32();
        // TODO find what the unknown3 is
        var doodadFuncId = stream.ReadUInt32();
        var msg = stream.ReadString();

        var reporter = Connection.ActiveChar;

        var bloodStainDoodad = reporter.ParentWorld?.GetDoodad(objId);
        if (bloodStainDoodad != null)
        {
            var criminalName = NameManager.Instance.GetCharacterName(bloodStainDoodad.OwnerId) ?? string.Empty;
            var crimeEvent = CrimeManager.Instance.ReportCrime(reporter, bloodStainDoodad, skillId, doodadNextFuncGroup, doodadFuncId, msg);
            if (crimeEvent != null)
            {
                Logger.Debug($"ReportCrime, ObjId: {objId}, Msg: {msg}, SkillId: {skillId}, DoodadFuncGroup: {doodadNextFuncGroup}, Unknown3: {doodadFuncId} (0x{doodadFuncId:X8}). Owner {criminalName} ({bloodStainDoodad.OwnerId}), OwnerDbId {bloodStainDoodad.OwnerDbId}");
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
