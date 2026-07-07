using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crime;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCrimeRecordsPacket(
        uint trialId,
        uint unknown1,
        int totalCount,
        int thisCount,
        IEnumerable<CrimeEvent> evidenceEntries)
        : GamePacket(SCOffsets.SCCrimeRecordsPacket, 1)
    {
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(trialId);
            stream.Write(unknown1);
            stream.Write(totalCount);
            stream.Write(thisCount);
            foreach (var e in evidenceEntries) // Max 5
            {
                var victim = WorldManager.Instance.GetCharacterById(e.Victim) ?? Character.Load(e.Victim);
                var reporterName = NameManager.Instance.GetCharacterName(e.Reporter);
                var msg = string.IsNullOrWhiteSpace(e.Msg) ? string.Empty : e.Msg.Substring(0, Math.Min(e.Msg.Length, 200));
                stream.Write(e.Id);
                stream.Write(e.Victim);
                stream.Write(victim?.Name ?? string.Empty);
                stream.Write(e.Reporter);
                stream.Write(reporterName);
                stream.Write((uint)(victim?.Faction?.Id ?? 0u));
                stream.Write((uint)(victim?.Expedition?.Id ?? 0u));
                stream.Write((byte)e.CrimeKind);
                stream.Write(e.DoodadTemplate);
                stream.Write(e.UsedSkillId);
                stream.Write(Helpers.ConvertLongX(e.Position.X));
                stream.Write(Helpers.ConvertLongY(e.Position.Y));
                stream.Write(e.Position.Z);
                stream.Write(msg);
                stream.Write(e.ReportTime);
            }
            return stream;
        }
    }
}
