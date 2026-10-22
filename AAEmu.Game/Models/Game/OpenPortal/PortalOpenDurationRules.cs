using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.OpenPortal;

/// <summary>
/// How long a cast worldgate (enter/exit NPC pair) stays open.
/// <para>
/// Shipped content has no lifetime column: <c>open_portal_effects</c> is only
/// distance / enter / exit / faction_permission, reagents have no duration, portal
/// NPC rows carry no life timer, and no <c>content_configs</c> name covers it.
/// The skill description for every <c>OpenPortalEffect</c> skill is the only
/// authored source: 30 seconds normally, 60 seconds when the caster's Death
/// (Occultism) ability is level 20+.
/// </para>
/// </summary>
public static class PortalOpenDurationRules
{
    /// <summary>Base open time from the OpenPortal skill description.</summary>
    public static readonly TimeSpan BaseOpen = TimeSpan.FromSeconds(30);

    /// <summary>Extended open time when Death ability is at or above <see cref="ExtendedAbilityLevel"/>.</summary>
    public static readonly TimeSpan ExtendedOpen = TimeSpan.FromSeconds(60);

    /// <summary>Death (Occultism) ability level that unlocks the extended open time.</summary>
    public const byte ExtendedAbilityLevel = 20;

    /// <summary>Ability the skill description names for the 60-second gate.</summary>
    public static AbilityType ExtendedAbility => AbilityType.Death;

    public static TimeSpan DurationFor(byte deathAbilityLevel) =>
        deathAbilityLevel >= ExtendedAbilityLevel ? ExtendedOpen : BaseOpen;
}
