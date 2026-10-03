using System.Numerics;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class EscapeMySlave : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        // TODO ...
        if (caster is Character player && targetObj is SkillCastPositionTarget skillCastPositionTarget)
        {
            Logger.Debug($"Special effects: EscapeMySlave value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}");

            if (!TryGetLanding(target, out var landing))
            {
                Logger.Warn($"{player.Name} using Rider's Escape without a resolved landing spot");
                return;
            }

            player.ParentWorld.SlaveManager.RidersEscape(player, landing, skillCastPositionTarget.PosRot);
        }
    }

    /// <summary>
    /// Where the hull lands: the stand Skill built from the packet (SetInitialTarget), which is what the
    /// cast range was measured against. The packet's own Pos* are not world coordinates when ObjId1 is
    /// set (they are local to that unit), so planting the hull at them would skip the range check.
    /// </summary>
    internal static bool TryGetLanding(BaseUnit target, out Vector3 landing)
    {
        if (target is { ObjId: uint.MaxValue, Transform: not null })
        {
            landing = target.Transform.World.Position;
            return true;
        }

        landing = default;
        return false;
    }
}
