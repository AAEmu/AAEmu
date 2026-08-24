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

    private static readonly Dictionary<SpecialType, bool> ImplementedCache = [];

    /// <summary>
    /// Whether an action class exists for this special type. Callers use it to tell "the skill did
    /// something" apart from "the skill was a no-op", which matters before charging the player for
    /// the cast.
    /// </summary>
    public static bool IsImplemented(SpecialType specialType)
    {
        lock (ImplementedCache)
        {
            if (ImplementedCache.TryGetValue(specialType, out var known))
                return known;

            var exists = ResolveActionType(specialType) != null;
            ImplementedCache[specialType] = exists;
            return exists;
        }
    }

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

        var classType = ResolveActionType(SpecialEffectTypeId);
        if (classType == null)
        {
            // We don't need to log every missing effect as some are client-sided
            if (SpecialEffectTypeId == SpecialType.Projectile)
                return;
            Logger.Warn("Unknown special effect: {0}", SpecialEffectTypeId);
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
