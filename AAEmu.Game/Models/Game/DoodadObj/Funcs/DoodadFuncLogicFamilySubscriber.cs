using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Phase-func: doodad is listening for logic-family <see cref="FamilyId"/>.
/// Does not gate the phase by itself (no next_phase column in data); paired content uses
/// Pulse / PulseTrigger to advance (e.g. charged harpoons).
/// </summary>
public class DoodadFuncLogicFamilySubscriber : DoodadPhaseFuncTemplate
{
    public uint FamilyId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        if (owner != null)
            owner.ListeningLogicFamilyId = FamilyId;

        Logger.Trace("DoodadFuncLogicFamilySubscriber: FamilyId {0} ownerTpl={1}", FamilyId, owner?.TemplateId);
        return false;
    }
}
