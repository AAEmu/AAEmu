using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// How an item template is picked up out of a loot window.
/// <para>
/// <c>items.auto_loot</c> marks the items that are not loot in the sense of "something to win": the
/// shipped rows are the collection orbs and collection ornaments, which are the reward for having
/// collected something rather than a prize from a kill. Such an item is handed over without a roll
/// and without a winner being recorded against it, because there is nothing to roll over - the
/// player's own collection produced it.
/// </para>
/// <para>
/// The rule only removes the roll. Which player receives an auto-loot item still follows the group's
/// loot method, so public loot stays first-come and a rotate-winner or loot-master group still hands
/// the item to the player its rules pick. An item is never taken away from a group that would
/// otherwise have rolled for it.
/// </para>
/// </summary>
public static class ItemLootRules
{
    /// <summary>
    /// Whether this item is handed over without a roll. False for every template that does not set
    /// <c>items.auto_loot</c>, which is all but ten of the shipped rows.
    /// </summary>
    public static bool IsAutoLoot(ItemTemplate template) => template?.AutoLoot == true;

    /// <summary>
    /// Whether taking this item has to be contested. A roll is only ever mandatory because of the
    /// team's own rules - a grade floor, or a bind-on-pickup item - and an auto-loot item is
    /// exempt from both, because neither of them is about a prize.
    /// </summary>
    public static bool RequiresRoll(ItemTemplate template, bool rollForGrade, bool rollForBindOnPickup) =>
        !IsAutoLoot(template) && (rollForGrade || rollForBindOnPickup);
}
