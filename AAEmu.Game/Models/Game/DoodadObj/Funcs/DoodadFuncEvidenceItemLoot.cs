using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Evidence left at the scene of a crime (bloodstain, footprint). The client loots it with its own
/// pickup skill - 증거물 줍기, carried on <c>doodad_func_evidence_item_loots.skill_id</c> - and that
/// skill advances the evidence to the chain row's next phase, exactly like the erase gate
/// (<see cref="DoodadFuncSkillHit"/>) sitting next to it in the same phase group.
/// </summary>
/// <remarks>
/// The crime points are applied once by <c>CrimeManager.ReportCrime</c>, which reads this same
/// template's <see cref="CrimeKindId"/> / <see cref="CrimeValue"/> when the report itself is
/// processed. Adding points here as well would double-count every report, so this func only
/// recognises the loot skill and never touches the criminal's record.
/// </remarks>
public class DoodadFuncEvidenceItemLoot : DoodadFuncTemplate
{
    // doodad_funcs + doodad_func_evidence_item_loots
    public uint SkillId { get; set; }
    public short CrimeValue { get; set; }
    public uint CrimeKindId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        owner.ToNextPhase = Recognizes(SkillId, skillId);
    }

    /// <summary>
    /// True when the incoming skill is the pickup skill this evidence row names. An unset row skill
    /// (0) never matches, so a stray cast cannot consume the evidence.
    /// </summary>
    public static bool Recognizes(uint lootSkillId, uint incomingSkillId) =>
        lootSkillId != 0 && lootSkillId == incomingSkillId;
}
