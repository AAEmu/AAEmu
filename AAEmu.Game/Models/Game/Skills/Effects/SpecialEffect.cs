using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpecialEffect : EffectTemplate
{
    public SpecialType SpecialEffectTypeId { get; set; }
    public int Value1 { get; set; }
    public int Value2 { get; set; }
    public int Value3 { get; set; }
    public int Value4 { get; set; }
    public int Value5 { get; set; }
    public int Value6 { get; set; }
    public int Value7 { get; set; }

    public override bool OnActionTime => false;

    /// <summary>
    /// Whether a cast of this type does something the player should be charged for: World executes it
    /// (<see cref="SpecialEffectOwnership.Gameplay"/>), or the client plays it and World is right to
    /// stay out (<see cref="SpecialEffectOwnership.ClientVisual"/>). An action class on its own proves
    /// nothing, since several only log; <see cref="SpecialEffectOwnershipRules"/> is the answer, and
    /// <see cref="Skill.IsPureNoOpCast"/> reads it before charging reagents.
    /// </summary>
    public static bool IsImplemented(SpecialType specialType) =>
        SpecialEffectOwnershipRules.CountsAsExecuted(SpecialEffectOwnershipRules.Classify(specialType));

    /// <summary>Whether an action class exists under SpecialEffects for the type. The tests pin the table against it.</summary>
    public static bool HasActionClass(SpecialType specialType) => ResolveActionType(specialType) != null;

    private static Type ResolveActionType(SpecialType specialType)
    {
        return Type.GetType("AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects." + specialType);
    }

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (source == null) return;

        Logger.ConditionalTrace(
            "SpecialEffect, Special: {0}, Values: [{1}, {2}, {3}, {4}, {5}, {6}, {7}]",
            SpecialEffectTypeId, Value1, Value2, Value3, Value4, Value5, Value6, Value7);

        var ownership = SpecialEffectOwnershipRules.Classify(SpecialEffectTypeId);
        if (ownership == SpecialEffectOwnership.ClientVisual)
            return; // the client plays it from the fired skill or the plot event; nothing runs here

        var classType = ResolveActionType(SpecialEffectTypeId);
        if (classType == null)
        {
            if (ownership == SpecialEffectOwnership.Gameplay)
                Logger.Warn("Special effect {0} is classified gameplay but has no action class", SpecialEffectTypeId);
            else
                Logger.Debug("Unsupported special effect {0} on skill {1}: nothing executed",
                    SpecialEffectTypeId, source.Skill?.Template?.Id ?? 0);
            return;
        }

        var action = (SpecialEffectAction)Activator.CreateInstance(classType);
        void ExecuteAction() => action?.Execute(caster, casterObj, target, targetObj, castObj, source.Skill,
            skillObject, time, Value1, Value2, Value3, Value4, Value5, Value6, Value7);

        var repeatCount = source.Skill?.Template.EffectRepeatCount ?? 0;
        if (repeatCount > 1)
        {
            ExecuteAction();
            var tick = TimeSpan.FromMilliseconds(Math.Max(1, source.Skill.Template.EffectRepeatTick));
            global::AAEmu.Game.Core.Managers.TaskManager.Instance.Schedule(
                new global::AAEmu.Game.Models.Tasks.Skills.SpecialEffectRepeatTask(ExecuteAction), tick, tick,
                repeatCount - 1);
        }
        else
        {
            ExecuteAction();
        }
    }
}
