using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

using GameSkillResult = Static.SkillResult;

/// <summary>
/// Server-side enforcement of the disabling buff states: what a rooted, stunned, sleeping or silenced
/// unit may still do.
/// </summary>
/// <remarks>
/// The states themselves are already tracked on the buff templates and already used in two places —
/// <c>Buffs.AddBuff</c> interrupts a cast when stun, silence or sleep lands and dismounts on
/// stun/sleep/root, and <c>LeapSkillController.MoveTowards</c> refuses to advance a stunned, sleeping,
/// rooted, knocked-down or fastened unit. Neither of them covers the two things a player actually does
/// with a disabling effect on: the client's move packet and the client's cast request. Both were
/// accepted, so a rooted player walked and a silenced player cast.
/// <para>
/// This class is the single reading of "what does the state forbid", so the packet gate and the cast
/// gate cannot drift apart, and it deliberately keeps the exact membership the two existing sites use:
/// <list type="bullet">
/// <item><b>Movement</b> is blocked by <c>stun</c>, <c>sleep</c> and <c>root</c> — the same three flags
/// that dismount a rider in <c>Buffs.AddBuff</c>. The client shows all three as a loss of control.</item>
/// <item><b>Casting</b> is blocked by <c>silence</c>, <c>sleep</c> and <c>stun</c> — the same three that
/// interrupt the cast already in flight there. <c>crippled</c> is deliberately not in the cast set:
/// 127 shipped buffs carry it and it means "cannot move", not "cannot act", and it is not one of the
/// three states <c>InterruptSkills</c> responds to.</item>
/// </list>
/// A unit with none of the flags reads as <see cref="EnforcedState.None"/> and takes exactly the path it
/// took before these gates existed; the flags are read once per packet, and only a flag that is set can
/// change any outcome.
/// </para>
/// </remarks>
public static class CrowdControlRules
{
    /// <summary>The disabling flags currently on the unit, read off its live buffs.</summary>
    /// <remarks>
    /// A buff counts from the moment it is in the list — <c>Buffs.AddBuff</c> interrupts the in-flight
    /// cast in the same block that subscribes it — so the two gates and the interrupt agree on which
    /// instant the state begins.
    /// </remarks>
    public readonly record struct EnforcedState(
        bool IsStunned,
        bool IsAsleep,
        bool IsSilenced,
        bool IsRooted)
    {
        public static EnforcedState None => default;

        /// <summary>Any of the three states that take movement away.</summary>
        public bool CannotMove => IsStunned || IsAsleep || IsRooted;

        /// <summary>Any of the three states that take casting away.</summary>
        public bool CannotCast => IsStunned || IsAsleep || IsSilenced;
    }

    /// <summary>
    /// Whether this state refuses a movement packet. Only the unit whose own buffs are being read can
    /// answer this, so callers gate on the mover and never on the unit being moved.
    /// </summary>
    public static bool BlocksMovement(in EnforcedState state) => state.CannotMove;

    /// <summary>
    /// The failure a cast by this state produces, or <see langword="null"/> when the cast may proceed.
    /// </summary>
    /// <remarks>
    /// The results are the client's own: <c>0x13 skill_cannot_cast_in_stun</c> for stun and for sleep
    /// (the client's result table has no sleep-specific entry and it bars a cast identically), and
    /// <c>0x17 skill_silence</c> for silence. Silence is checked last so a unit that is both stunned and
    /// silenced on a silence-permitting skill reports the stun, which is the state the client is drawing.
    /// </remarks>
    public static GameSkillResult? RejectCast(in EnforcedState state)
    {
        if (state.IsStunned || state.IsAsleep)
            return GameSkillResult.CannotCastInStun;
        if (state.IsSilenced)
            return GameSkillResult.Silence;

        return null;
    }

    /// <summary>Same answer as <see cref="RejectCast"/>, with the failure spelled out for the callers
    /// that have to log or report a result either way.</summary>
    public static GameSkillResult RejectCast(in EnforcedState state, GameSkillResult whenAllowed) =>
        RejectCast(state) ?? whenAllowed;

    /// <summary>
    /// <see cref="RejectCast(in EnforcedState)"/> evaluated against the skill's own permission bits.
    /// </summary>
    /// <remarks>
    /// A skill may declare that it is usable in a given state (<c>skills.use_condition_bits</c>, bits 6,
    /// 13 and 15 — a mask cannot require stun *and* sleep *and* silence at once, so those bits are a
    /// permission set). Every one of the 2 buffs that grant such a cast is a "break out of it" ability:
    /// 강인한 의지, 활력 방패, 기선 제압, each of which has to be pressable while the state that it clears
    /// is on. Without the permission the client greys the icon out, so this is refused for exactly the
    /// cases the client refuses, and allowed for exactly the ones it allows.
    /// <para>
    /// The bit opt-outs are parameters rather than a template read so this module stays independent of
    /// the <c>use_condition_bits</c> gate (D3) while still letting a cast that made it past that gate
    /// through here. A caller that has no bit information passes <see langword="false"/>, which is the
    /// strict reading.
    /// </para>
    /// </remarks>
    public static GameSkillResult? RejectCast(
        in EnforcedState state,
        bool allowedWhileStunned,
        bool allowedWhileAsleep,
        bool allowedWhileSilenced)
    {
        if (state.IsStunned && !allowedWhileStunned)
            return GameSkillResult.CannotCastInStun;
        if (state.IsAsleep && !allowedWhileAsleep)
            return GameSkillResult.CannotCastInStun;
        if (state.IsSilenced && !allowedWhileSilenced)
            return GameSkillResult.Silence;

        return null;
    }

    /// <summary>Reads the disabling state off a live unit's buffs.</summary>
    public static EnforcedState ReadState(Unit unit)
    {
        if (unit?.Buffs == null)
            return EnforcedState.None;

        return new EnforcedState(
            unit.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun),
            unit.Buffs.HasEffectsMatchingCondition(e => e.Template.Sleep),
            unit.Buffs.HasEffectsMatchingCondition(e => e.Template.Silence),
            unit.Buffs.HasEffectsMatchingCondition(e => e.Template.Root));
    }
}
