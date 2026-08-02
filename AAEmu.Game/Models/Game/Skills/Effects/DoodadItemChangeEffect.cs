using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Sows a crop into the farm plot the skill was cast at.
///
/// This is the whole of a seed skill: all 160 of them — "감자 씨앗 뿌리기" and the rest — carry exactly one
/// effect, this one, and target a doodad (skills.target_type_id 8). The plot lists the crops it accepts as
/// <see cref="DoodadFuncItemChanger"/> entries in its current phase, and <see cref="Idx"/> is the position of
/// the one this skill sows.
///
/// The mapping is exact in the shipped data: in func group 24993 the item changers run potato, cucumber,
/// carrot, onion, and the four sow skills 29236/29237/29238/29239 carry idx 0/1/2/3 in that same order.
/// </summary>
public class DoodadItemChangeEffect : EffectTemplate
{
    /// <summary>Position of the DoodadFuncItemChanger to apply within the target doodad's current phase.</summary>
    public int Idx { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Character)
            return;

        if (target is not Doodad doodad)
        {
            Logger.Warn($"DoodadItemChangeEffect: target is {target?.GetType().Name ?? "null"}, expected a Doodad");
            return;
        }

        var changers = new List<DoodadFuncItemChanger>();
        foreach (var phaseFunc in doodad.CurrentPhaseFuncs)
        {
            if (phaseFunc.FuncType != nameof(DoodadFuncItemChanger))
                continue;

            if (DoodadManager.Instance.GetPhaseFuncTemplate(phaseFunc.FuncId, phaseFunc.FuncType) is DoodadFuncItemChanger changer)
                changers.Add(changer);
        }

        if (Idx < 0 || Idx >= changers.Count)
        {
            Logger.Warn($"DoodadItemChangeEffect: doodad {doodad.TemplateId} phase {doodad.FuncGroupId} has {changers.Count} item changers, no index {Idx}");
            return;
        }

        changers[Idx].Apply(caster, doodad);
    }
}
