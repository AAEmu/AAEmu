using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Phase-func signal: this doodad currently provides logic-family <see cref="FamilyId"/>.
/// Subscribers with the same family id can poll <see cref="Doodad.ActiveLogicFamilyId"/>
/// on nearby providers. Grimghast treb→harpoon unlock is driven by Pulse, not this family
/// (provider families 40006/40008 vs harpoon subscriber 40007 do not match in data).
/// </summary>
public class DoodadFuncLogicFamilyProvider : DoodadPhaseFuncTemplate
{
    public uint FamilyId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        if (owner != null)
            owner.ActiveLogicFamilyId = FamilyId;

        Logger.Trace("DoodadFuncLogicFamilyProvider: FamilyId {0} ownerTpl={1}", FamilyId, owner?.TemplateId);
        return false;
    }
}
