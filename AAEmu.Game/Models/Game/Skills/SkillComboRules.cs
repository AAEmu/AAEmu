﻿using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Combo special (type 48) names the next hold-hit. The client starts that skill
/// when its GCD is up. World does not schedule or delay those hits.
/// </summary>
/// <remarks>
/// Follow-up skill ids are not separate book entries. The client swaps the bar from the Combo
/// effect and sends an ordinary <c>CSStartSkill</c> for the next id. That id is castable only while
/// an open window from the prior accepted hit remains — holding the root is not enough, or a later
/// hit with its own short cooldown could be cast without the root. Other skills cast in between do
/// not close that window.
/// </remarks>
public static class SkillComboRules
{
    public const int MaxChainLength = 8;

    public static bool TryGetComboFollowUp(SkillTemplate template, out uint nextSkillId, out int delayMs)
    {
        nextSkillId = 0;
        delayMs = 0;
        if (template?.Effects == null)
            return false;
        foreach (var effect in template.Effects)
        {
            if (effect.Template is not SpecialEffect special)
                continue;
            if (special.SpecialEffectTypeId != SpecialType.Combo || special.Value1 <= 0)
                continue;
            nextSkillId = (uint)special.Value1;
            delayMs = Math.Max(0, special.Value2);
            return true;
        }
        return false;
    }

    public static uint? NextFollowUp(uint skillId)
    {
        var template = SkillManager.Instance?.GetSkillTemplate(skillId);
        return TryGetComboFollowUp(template, out var next, out _) ? next : null;
    }

    public static List<uint> WalkChain(uint firstFollowUp, Func<uint, uint?> nextFollowUp)
    {
        var chain = new List<uint>();
        var id = firstFollowUp;
        var guard = 0;
        while (id != 0 && guard++ < MaxChainLength)
        {
            chain.Add(id);
            id = nextFollowUp?.Invoke(id) ?? 0;
        }
        return chain;
    }

    /// <summary>
    /// True when <paramref name="maybeFollowUp"/> is a later hold-hit of
    /// <paramref name="rootSkillId"/>. Used so a repeat of the root does not
    /// cancel an in-flight follow-up plot.
    /// </summary>
    public static bool IsComboFollowUpOf(uint rootSkillId, uint maybeFollowUp, Func<uint, uint?> nextFollowUp)
    {
        if (rootSkillId == 0 || maybeFollowUp == 0 || rootSkillId == maybeFollowUp)
            return false;
        foreach (var id in WalkChain(nextFollowUp?.Invoke(rootSkillId) ?? 0, nextFollowUp))
        {
            if (id == maybeFollowUp)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="skillId"/> is held, or is a Combo follow-up of any held skill.
    /// </summary>
    public static bool IsHeldOrComboFollowUp(
        uint skillId,
        IEnumerable<uint> heldSkillIds,
        Func<uint, uint?> nextFollowUp)
    {
        if (skillId == 0 || heldSkillIds == null)
            return false;

        foreach (var held in heldSkillIds)
        {
            if (held == skillId)
                return true;
            if (IsComboFollowUpOf(held, skillId, nextFollowUp))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Combo window (ms) from the skill's Combo special <c>value2</c> plus the time until the
    /// client can send the next link, or 0 when it has no Combo link.
    /// </summary>
    /// <remarks>
    /// The window is measured from when the cast is accepted. Ready delay matches the press-armed
    /// shared GCD: cast bars scale by cast speed; the GCD base follows equipped weapon speed and
    /// <see cref="SkillGcdRules.AttackSpeedGcdMultiplier"/> (cast speed does not scale the GCD).
    /// </remarks>
    public static int ComboWindowMs(
        uint skillId,
        int equippedWeaponGcdSpeed = 0,
        float castTimeMul = 1f,
        float globalCooldownMul = 100f,
        long attackSpeedRating = 0)
    {
        var template = SkillManager.Instance?.GetSkillTemplate(skillId);
        if (template == null || !TryGetComboFollowUp(template, out _, out var ms) || ms <= 0)
            return 0;
        return ms + FollowUpReadyDelayMs(
            template, equippedWeaponGcdSpeed, castTimeMul, globalCooldownMul, attackSpeedRating);
    }

    /// <summary>
    /// Milliseconds after accept before the client may send this skill's Combo follow-up.
    /// </summary>
    /// <param name="equippedWeaponGcdSpeed">
    /// Equipped holdable speed for the skill's <c>weapon_gcd_id</c> slot (15/16/17), or 0 when the
    /// skill does not select a weapon slot / the slot is empty.
    /// </param>
    /// <param name="castTimeMul">Caster cast-speed factor (baseline 1). Scales <c>casting_time</c> only.</param>
    /// <param name="globalCooldownMul">
    /// Caster attr-74 interval percent (baseline 100). Shared GCD fallback when attack-speed rating is 0.
    /// </param>
    /// <param name="attackSpeedRating">Caster attr-218 sum; 0 uses <paramref name="globalCooldownMul"/>.</param>
    public static int FollowUpReadyDelayMs(
        SkillTemplate template,
        int equippedWeaponGcdSpeed = 0,
        float castTimeMul = 1f,
        float globalCooldownMul = 100f,
        long attackSpeedRating = 0)
    {
        if (template == null)
            return 0;

        var castMul = Math.Max(0f, castTimeMul);
        var castMs = (int)Math.Round(Math.Max(0, template.CastingTime) * castMul);

        var usesWeaponSpeed = template.WeaponGcdId > 0;
        var weaponGcdSpeed = usesWeaponSpeed ? Math.Max(0, equippedWeaponGcdSpeed) : 0;
        var useDefaultGcd = template.DefaultGcd ||
                            (usesWeaponSpeed && weaponGcdSpeed <= 0 && template.CustomGcd <= 0);
        var gcdBase = SkillGcdRules.ResolveSharedGcd(
            template.CustomGcd, useDefaultGcd, weaponGcdSpeed, isNpc: false);
        var gcdMul = SkillGcdRules.AttackSpeedGcdMultiplier(attackSpeedRating, globalCooldownMul);
        var gcdMs = (int)Math.Round(gcdBase * gcdMul);

        return Math.Max(castMs, gcdMs);
    }

    /// <summary>
    /// True when <paramref name="skillId"/> is exactly the next Combo link after
    /// <paramref name="lastFiredSkillId"/> and still inside that link's window.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="IsOpenComboFollowUp"/> for live casts: an intervening skill must not
    /// close a still-open return/hold window (dash → attack → return).
    /// </remarks>
    public static bool IsNextComboWithinWindow(
        uint skillId,
        uint lastFiredSkillId,
        DateTime lastFiredAt,
        DateTime now,
        Func<uint, uint?> nextFollowUp,
        Func<uint, int> windowMsFor)
    {
        if (skillId == 0 || lastFiredSkillId == 0 || nextFollowUp == null || windowMsFor == null)
            return false;
        if (nextFollowUp(lastFiredSkillId) != skillId)
            return false;

        var window = windowMsFor(lastFiredSkillId);
        if (window <= 0)
            return false;

        return (now - lastFiredAt).TotalMilliseconds <= window;
    }

    /// <summary>
    /// True when <paramref name="skillId"/> still has an unexpired Combo follow-up window.
    /// </summary>
    public static bool IsOpenComboFollowUp(
        uint skillId,
        IReadOnlyDictionary<uint, DateTime> followUpExpiresAt,
        DateTime now)
    {
        if (skillId == 0 || followUpExpiresAt == null)
            return false;
        return followUpExpiresAt.TryGetValue(skillId, out var expiresAt) && now <= expiresAt;
    }

    /// <summary>
    /// After an accepted cast: drop a consumed follow-up window for this id, then open the next
    /// link's window when this skill names one. Other open windows stay (intervening casts).
    /// </summary>
    public static void ApplyAcceptedCast(
        uint skillId,
        DateTime now,
        IDictionary<uint, DateTime> followUpExpiresAt,
        Func<uint, uint?> nextFollowUp,
        Func<uint, int> windowMsFor)
    {
        if (skillId == 0 || followUpExpiresAt == null || nextFollowUp == null || windowMsFor == null)
            return;

        followUpExpiresAt.Remove(skillId);

        var next = nextFollowUp(skillId);
        if (next is not uint nextId || nextId == 0)
            return;

        var window = windowMsFor(skillId);
        if (window <= 0)
            return;

        followUpExpiresAt[nextId] = now.AddMilliseconds(window);
    }

    /// <summary>
    /// Drops expired Combo follow-up windows.
    /// </summary>
    public static void PruneExpiredFollowUps(IDictionary<uint, DateTime> followUpExpiresAt, DateTime now)
    {
        if (followUpExpiresAt == null || followUpExpiresAt.Count == 0)
            return;

        List<uint> stale = null;
        foreach (var (id, expiresAt) in followUpExpiresAt)
        {
            if (now > expiresAt)
                (stale ??= []).Add(id);
        }

        if (stale == null)
            return;
        foreach (var id in stale)
            followUpExpiresAt.Remove(id);
    }

    /// <summary>
    /// On a cancelled accept: drop the follow-up window this skill opened so a refunded root cannot
    /// unlock the next hold-hit for free.
    /// </summary>
    public static void CloseFollowUpOpenedBy(
        uint skillId,
        IDictionary<uint, DateTime> followUpExpiresAt,
        Func<uint, uint?> nextFollowUp)
    {
        if (skillId == 0 || followUpExpiresAt == null || nextFollowUp == null)
            return;
        var next = nextFollowUp(skillId);
        if (next is uint nextId && nextId != 0)
            followUpExpiresAt.Remove(nextId);
    }

    /// <summary>
    /// Hold-to-repeat (<c>start_autoattack</c>) sometimes keeps sending the root id even while a
    /// Combo follow-up window is open (client never swaps the bar). Map that press onto the open
    /// link so the chain advances; a press that already names an open follow-up is left alone.
    /// </summary>
    public static uint RemapHoldRepeatToOpenFollowUp(
        uint skillId,
        bool startAutoAttack,
        Func<uint, uint?> nextFollowUp,
        IReadOnlyDictionary<uint, DateTime> followUpExpiresAt,
        DateTime now)
    {
        if (skillId == 0 || !startAutoAttack || nextFollowUp == null || followUpExpiresAt == null)
            return skillId;

        if (IsOpenComboFollowUp(skillId, followUpExpiresAt, now))
            return skillId;

        var next = nextFollowUp(skillId);
        if (next is not uint first || first == 0)
            return skillId;

        foreach (var id in WalkChain(first, nextFollowUp))
        {
            if (IsOpenComboFollowUp(id, followUpExpiresAt, now))
                return id;
        }

        return skillId;
    }
}
