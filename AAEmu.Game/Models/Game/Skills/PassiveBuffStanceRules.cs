using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Stance-gated passives own a <c>Started</c> buff trigger whose
/// <c>owner_buff_tag_id</c> / <c>owner_no_buff_tag_id</c> keys off the equip stance
/// (two-hand / dual-wield / shield). The child effect is chosen only when that trigger
/// fires, so a mid-session gear change must re-apply the passive after the stance buff
/// updates — otherwise the child from the first learn sticks forever.
/// </summary>
public static class PassiveBuffStanceRules
{
    /// <summary>
    /// True when any <c>Started</c> trigger on the passive's buff is gated by an owner stance tag.
    /// </summary>
    public static bool HasStanceGatedStartedTrigger(IEnumerable<BuffTriggerTemplate> triggers)
    {
        if (triggers == null)
            return false;

        foreach (var trigger in triggers)
        {
            if (trigger == null || trigger.Kind != BuffEventTriggerKind.Started)
                continue;
            if (trigger.OwnerBuffTagId != 0 || trigger.OwnerNoBuffTagId != 0)
                return true;
        }

        return false;
    }
}
