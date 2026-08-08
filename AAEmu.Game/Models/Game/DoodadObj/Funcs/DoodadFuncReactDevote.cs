using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Contribution counter driven by skill hits rather than direct interaction, used for the later
/// Auroria base ranks. Every hit of <see cref="SkillId"/> raises the counter by one; reaching
/// <see cref="Count"/> moves the doodad to <see cref="NextPhase"/>.
/// </summary>
/// <remarks>
/// The counting skills (31315 Nuian / 30929 Haranyan) are effect-less 20m radius AoEs fired by
/// quest components on completion, so finishing a colonization quest near the base lands one count.
/// This is stored as a phase func, but the actual counting is driven from Doodad.OnSkillHit since
/// the phase func interface carries no skill id.
/// </remarks>
public class DoodadFuncReactDevote : DoodadPhaseFuncTemplate
{
    // doodad_func_react_devotes
    public uint SkillId { get; set; }
    /// <summary>Number of skill hits required to advance to <see cref="NextPhase"/></summary>
    public int Count { get; set; }
    public string TooltipText { get; set; }
    public int NextPhase { get; set; }

    /// <summary>
    /// Runs when the doodad enters this phase.
    /// </summary>
    /// <remarks>
    /// Deliberately does NOT reset the counter. Restoring a persistent doodad assigns FuncGroupId and
    /// Data first, then calls InitDoodad, which runs DoChangePhase against the phase it is already in
    /// and therefore reaches this method - zeroing here would wipe the restored progress on every
    /// server start. The counter is instead cleared by whichever func advances the phase, which is
    /// the only point where it genuinely needs to start over.
    /// </remarks>
    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncReactDevote: SkillId {0}, Count {1}, ObjId {2}, at {3}", SkillId, Count, owner.ObjId, owner.Data);

        return false; // don't stop the remaining phase funcs
    }

    /// <summary>
    /// Registers one skill hit. Returns true when the target count is reached and the caller
    /// should move the doodad to <see cref="NextPhase"/>.
    /// </summary>
    public bool RegisterHit(Doodad owner)
    {
        var contributions = owner.Data + 1;

        if (contributions < Count)
        {
            Logger.Debug($"DoodadFuncReactDevote: doodad {owner.TemplateId} (objId {owner.ObjId}) at {contributions}/{Count}, {Count - contributions} left");
            DoodadFuncDevote.PublishProgress(owner, contributions);
            return false;
        }

        Logger.Info($"DoodadFuncReactDevote: doodad {owner.TemplateId} (objId {owner.ObjId}) reached {Count} hits of skill {SkillId}, advancing to phase {NextPhase}");
        DoodadFuncDevote.PublishProgress(owner, 0);
        return true;
    }
}
