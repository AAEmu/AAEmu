using AAEmu.Game.Core.Managers;
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
    /// The window is measured from when the cast is accepted. The client cannot send the follow-up
    /// until that hit's cast bar and/or shared GCD have finished — cast-time hits used to burn
    /// <c>value2</c> before the next press; plot-only chains with <c>custom_gcd</c> longer than
    /// <c>value2</c> (Mana Bolt under Serpent Glare) did the same on the GCD. Adding
    /// <c>max(casting_time, shared GCD)</c> keeps the next link open through that wait and still
    /// closes it so a later link cannot be sent on its own.
    /// </remarks>
    public static int ComboWindowMs(uint skillId)
    {
        var template = SkillManager.Instance?.GetSkillTemplate(skillId);
        if (template == null || !TryGetComboFollowUp(template, out _, out var ms) || ms <= 0)
            return 0;
        return ms + FollowUpReadyDelayMs(template);
    }

    /// <summary>
    /// Milliseconds after accept before the client may send this skill's Combo follow-up.
    /// </summary>
    public static int FollowUpReadyDelayMs(SkillTemplate template)
    {
        if (template == null)
            return 0;
        var castMs = Math.Max(0, template.CastingTime);
        // Weapon-class GCD needs the caster's holdable; combo gates only see the template, and the
        // shipped caster chains that hit this path author custom_gcd outright.
        var gcdMs = SkillGcdRules.ResolveSharedGcd(
            template.CustomGcd, template.DefaultGcd, weaponGcdSpeed: 0, isNpc: false);
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
}
