using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// 172 move_to_saved_pos: recalls the caster to the point its marking buff captured.
/// </summary>
/// <remarks>
/// The only parameter is the marking buff id — 41487/45779 급습 pass 24610, 41964/45772/45778 급습 pass
/// 24947, 42012 마법진 이동 passes 19037 — and all nine rows of type 172 name one of the six
/// <c>buffs</c> rows that carry <c>save_pos = 't'</c>. The marking cast (40333/45592/47006 급습 for
/// 24610, 41756/45593 for 24947, all on a 24 s or longer cooldown) applies that buff as the first effect
/// of its cast and names the 100 ms recall cast in a Combo effect, and the buff's own description is
/// "returns to the original position when the ambush skill is reused". That is why the point is captured
/// in <see cref="Units.Buffs.AddBuff"/> rather than when the recall is cast: by then the caster has
/// already moved. The buff is not consumed here — every one of these skills carries its own DispelEffect
/// for the marking buff's tag (4259 tag 4283 on 41487, 4260 tag 4284 on 41964, and so on).
/// <para>
/// The magic-circle half is dormant: buffs 19037/25850/25851 have <c>buff_effects</c> rows but no
/// <c>effects</c> row wraps them, so no shipped skill, plot or doodad applies them and only the 급습
/// pairs actually run.
/// </para>
/// </remarks>
public class MoveToSavedPos : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.MoveToSavedPos;

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
        if (caster is not Character character)
            return;

        var marking = character.Buffs.GetEffectFromBuffId((uint)value1);
        var saved = marking?.SavedPosition;
        if (!SavedPositionRules.CanReturnTo(saved, character.Transform.ZoneId, character.Transform.InstanceId))
        {
            Logger.Debug(
                "move_to_saved_pos: {0} has no usable point for buff {1} (marked={2})",
                character.Name,
                value1,
                saved.HasValue);
            return;
        }

        var point = saved.Value;
        // Same landing the return point and house recall effects use: it stands the character up, writes
        // the local transform, sends SCTeleportUnit and repaints the neighbourhood. stayInZone keeps it
        // from re-resolving the zone from coordinates that the guard above already proved local.
        SkillTeleportLanding.Apply(
            character,
            character.Transform.WorldId,
            point.ZoneId,
            point.InstanceId,
            point.X,
            point.Y,
            point.Z,
            point.YawRad,
            TeleportReason.MoveToLocation,
            stayInZone: true);
    }
}
