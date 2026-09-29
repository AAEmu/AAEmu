using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// What <c>items.proc_lifetime</c> and <c>items.proc_recharge_restrict_item_id</c> mean for a proc
/// bound to an item.
/// <para>
/// <c>proc_lifetime</c> is how many times the item's bound procs may fire before they are spent, and
/// <c>proc_recharge_restrict_item_id</c> names the item that puts them back. Only two shipped item
/// rows carry a lifetime at all (both are test gloves), and both name a recharge item, so every other
/// item's procs fire for as long as the item is worn.
/// </para>
/// <para>
/// The charge is a property of the worn item, not of the proc: the same proc bound to a second copy
/// of the same template has its own allowance. That is why these decisions take the item's template
/// and a count of the item's own fires, and why they can be asked without a unit in hand.
/// </para>
/// </summary>
public static class ItemProcLifetimeRules
{
    /// <summary>Whether this template's bound procs run out after a number of fires.</summary>
    public static bool HasLifetime(ItemTemplate template) => template?.ProcLifetime > 0;

    /// <summary>
    /// Whether a proc bound to this item may fire again after it has already fired
    /// <paramref name="firesSoFar"/> times.
    /// </summary>
    public static bool CanFire(ItemTemplate template, int firesSoFar) =>
        !HasLifetime(template) || firesSoFar < template.ProcLifetime;

    /// <summary>Whether the spent procs of this item can be put back by using another item.</summary>
    public static bool IsRechargeable(ItemTemplate template) =>
        HasLifetime(template) && template.ProcRechargeRestrictItemId > 0;

    /// <summary>
    /// Whether a proc binding of this item may be attached to a unit at all.
    /// </summary>
    /// <remarks>
    /// A lifetime-bound binding is not attached. A spent proc has to stop firing, and the count of a
    /// worn item's fires is not something the unit's proc set can answer today: the set is synced as
    /// one flat list of proc ids, with no record of which worn piece granted which, and there is no
    /// wire on which a remaining-charge count could be reported. Attaching the binding without that
    /// would give the two test gloves procs that never run out - the opposite of what the column
    /// asks for - so the binding is left off and the loader reports what it left off, by item id and
    /// by how many fires the column wanted.
    /// </remarks>
    public static bool BindingMayAttach(ItemTemplate template) => !HasLifetime(template);
}
