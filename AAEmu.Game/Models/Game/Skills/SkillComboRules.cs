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
/// effect and sends an ordinary <c>CSStartSkill</c> for the next id. That id is castable only as
/// the next link after the previous hit, inside that hit's Combo window — holding the root is not
/// enough, or a later hit with its own short cooldown could be cast without the root.
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
    /// Combo window (ms) from the skill's Combo special <c>value2</c> plus that skill's cast time,
    /// or 0 when it has no Combo link.
    /// </summary>
    /// <remarks>
    /// The window is measured from when the cast is accepted, which is before a cast-time hit
    /// finishes. Adding the cast time leaves the follow-up inside the window once the bar swaps,
    /// and still closes it so a later link cannot be sent on its own.
    /// </remarks>
    public static int ComboWindowMs(uint skillId)
    {
        var template = SkillManager.Instance?.GetSkillTemplate(skillId);
        if (template == null || !TryGetComboFollowUp(template, out _, out var ms) || ms <= 0)
            return 0;
        return ms + Math.Max(0, template.CastingTime);
    }

    /// <summary>
    /// True when <paramref name="skillId"/> is exactly the next Combo link after
    /// <paramref name="lastFiredSkillId"/> and still inside that link's window.
    /// </summary>
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
}
