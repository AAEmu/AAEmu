using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Evidence left at the scene of a crime (bloodstain, footprint). The client loots it with its own
/// pickup skill - 증거물 줍기, carried on <c>doodad_func_evidence_item_loots.skill_id</c> - and that
/// opens the report window the crime is filed from.
/// </summary>
/// <remarks>
/// The crime points are applied once by <c>CrimeManager.ReportCrime</c>, which reads this same
/// template's <see cref="CrimeKindId"/> / <see cref="CrimeValue"/> off the phase the evidence is
/// standing on when the report is processed, and then moves the evidence on to that func's own next
/// phase. Advancing the phase here, on the skill use, would take the func off the evidence before the
/// report arrives and the report would file <c>CrimeKind.None</c> with no points - which is why the
/// row completes from the client's report and nothing is applied here.
/// </remarks>
public class DoodadFuncEvidenceItemLoot : DoodadFuncTemplate
{
    // doodad_funcs + doodad_func_evidence_item_loots
    public uint SkillId { get; set; }
    public short CrimeValue { get; set; }
    public uint CrimeKindId { get; set; }

    /// <summary>The report the pickup opens is what completes this row; see the class remarks.</summary>
    public override bool CompletesFromClientPacket => true;

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (Recognizes(SkillId, skillId))
        {
            Logger.Trace($"Evidence {owner?.ObjId} looted with skill {skillId} - waiting for the report");
        }
    }

    /// <summary>
    /// True when the incoming skill is the pickup skill this evidence row names. An unset row skill
    /// (0) never matches, so a stray cast cannot consume the evidence.
    /// </summary>
    public static bool Recognizes(uint lootSkillId, uint incomingSkillId) =>
        lootSkillId != 0 && lootSkillId == incomingSkillId;
}
