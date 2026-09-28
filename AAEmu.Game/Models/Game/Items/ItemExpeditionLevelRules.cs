using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// The guild (expedition) level gate an item can carry on its own row.
/// <para>
/// <c>items.expedition_level</c> is the lowest guild level whose members may use the item.
/// A row that leaves it at 0 is not gated and always passes. Because the column holds one
/// number it is a floor: a guild at or above it may use the item, and a guild below it may
/// not. A character with no guild has level 0, so a gated item is refused rather than
/// silently allowed for guildless members.
/// </para>
/// This is deliberately not the skill requirement's two-bound band check: an item names a
/// single floor, so a guild that has outgrown the floor must still be admitted.
/// </summary>
public static class ItemExpeditionLevelRules
{
    /// <summary>
    /// Decides whether a character may use an item.
    /// </summary>
    /// <param name="template">The item template being used; a null template cannot be gated.</param>
    /// <param name="expeditionLevel">The using character's current guild level (0 when guildless).</param>
    /// <returns><c>true</c> when the item is ungated or the guild level satisfies it.</returns>
    public static bool AllowsUse(ItemTemplate template, uint expeditionLevel)
    {
        if (template?.ExpeditionLevel is not { } required || required == 0)
            return true;

        // The column is a single number, so it is a floor and not a band. The skill-side
        // requirement operator takes two bounds and admits a level only when it falls between
        // them; feeding it the same bound twice would admit level == required and nothing
        // above it, which would lock a member out of their own item the moment their guild
        // outgrew the floor.
        return expeditionLevel >= required;
    }
}
