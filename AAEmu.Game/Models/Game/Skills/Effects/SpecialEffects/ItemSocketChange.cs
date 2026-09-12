using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Socket change (special effect type 169): a conversion or upgrade stone turns the lunagems already
/// seated in a piece into other lunagems, without a roll and without touching the other sockets.
/// </summary>
/// <remarks>
/// <para>
/// The stone's use-skill casts with the stone as the item caster and the piece as the item target,
/// which is the same shape lunagem seating uses. <c>item_socket_changes</c> says, per stone, which
/// seated gem becomes which; the gear window's socket-upgrade tab only lets the player tick sockets
/// whose gem that stone can change, and sends the ticked ones as a bit mask in the cast's first
/// extra value (bit N is socket N).
/// </para>
/// <para>
/// One stone and <c>value1</c> labor (200 on every shipped stone) per changed socket. The labor
/// is what the stone's description says and what the tab shows next to its button; the stones
/// follow from the tab itself, which stops taking ticks once they equal the stones in the bag
/// (its <c>enableList</c> goes false at that point and comes back when another stone arrives).
/// The stones are not <c>use_skill_as_reagent</c> and their skill rows carry no
/// <c>consume_source_item</c>, so the skill system leaves them in the bag and this effect takes
/// them. Nothing is spent until both checks have passed. The result goes out on the item detail
/// packet, the way seating does; the socketing result packet closes the tab's run.
/// </para>
/// </remarks>
public class ItemSocketChange : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemSocketChange;

    /// <summary>The client tells the tab apart from a gem seating by this kind.</summary>
    private const byte KindSocketChange = 2;

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
        Logger.Debug("Special effects: ItemSocketChange value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (caster is not Character owner)
        {
            Logger.Error($"Special effects: ItemSocketChange caster {caster?.Id} is not a character");
            return;
        }

        if (casterObj is not SkillItem stoneSkillItem)
        {
            Logger.Error($"Special effects: ItemSocketChange casterObj {casterObj} is not a SkillItem");
            return;
        }

        if (targetObj is not SkillCastItemTarget skillTargetItem)
        {
            Logger.Error($"Special effects: ItemSocketChange targetObj {targetObj} is not a SkillCastItemTarget");
            return;
        }

        var targetItem = owner.Inventory.GetItemById(skillTargetItem.Id);
        var stone = owner.Inventory.GetItemById(stoneSkillItem.ItemId);
        if (targetItem is null || stone is null)
        {
            Logger.Warn($"Special effects: ItemSocketChange target {skillTargetItem.Id} or stone {stoneSkillItem.ItemId} not found");
            return;
        }

        if (targetItem is not EquipItem equipItem)
        {
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            return;
        }

        if (!ItemManager.Instance.IsSocketChangeStone(stone.TemplateId))
        {
            Logger.Warn($"Special effects: ItemSocketChange item {stone.TemplateId} has no item_socket_changes rows");
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            return;
        }

        // Which sockets the tab ticked, or every changeable one when nothing was sent.
        var mask = SelectedSockets(skillObject);
        var changes = new List<(int index, uint from, uint to)>();
        for (var i = 0; i < equipItem.GemIds.Length; i++)
        {
            if (mask != 0 && (mask & (1u << i)) == 0)
                continue;
            var seated = equipItem.GemIds[i];
            if (seated == 0)
                continue;
            var becomes = ItemManager.Instance.GetSocketChangeTarget(stone.TemplateId, seated);
            if (becomes != 0)
                changes.Add((i, seated, becomes));
        }

        if (changes.Count == 0)
        {
            // Nothing the stone can change was ticked (or seated). The tab greys those sockets
            // itself; this catches a stale window or a hand-built cast.
            owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
            return;
        }

        // Both checked before anything is spent.
        if (!owner.Inventory.CheckItems(SlotType.Inventory, stone.TemplateId, changes.Count))
        {
            owner.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return;
        }

        var labor = Math.Max(0, value1) * changes.Count;
        if (labor > 0 && owner.LaborPower + owner.LocalLaborPower < labor)
        {
            owner.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            return;
        }

        foreach (var (index, _, to) in changes)
            equipItem.GemIds[index] = to;

        if (labor > 0)
            owner.ChangeLabor((short)-Math.Min(labor, short.MaxValue), 0);

        // The stones are the cast's item and the skill system does not consume them (see above).
        var consumed = owner.Inventory.ConsumeItem([SlotType.Inventory], ItemTaskType.ItemSocketChange,
            stone.TemplateId, changes.Count, stone);
        if (consumed < changes.Count)
            Logger.Warn($"ItemSocketChange: {owner.Name} had {consumed} of {changes.Count} stones ({stone.TemplateId}) taken");

        equipItem.IsDirty = true;

        // The piece goes out on the detail packet, as seating does; the tab re-reads it from the bag.
        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));
        if (equipItem.SlotType == SlotType.Equipment)
            owner.UpdateGearBonuses(null, null);

        // Close the tab's run. The type is the gem the first ticked socket became: the client's
        // ITEM_SOCKET_UPGRADE notice names one gem.
        owner.SendPacket(new SCItemSocketingLunagemResultPacket(1, equipItem.Id, changes[0].to, KindSocketChange, true));

        Logger.Info("ItemSocketChange: {0} changed {1} socket(s) on item {2} with stone {3}: {4}",
            owner.Name, changes.Count, equipItem.Id, stone.TemplateId,
            string.Join(", ", changes.Select(c => $"[{c.index}] {c.from}->{c.to}")));

        owner.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);
    }

    /// <summary>
    /// The socket bit mask the gear window sent with the cast, or 0 when the cast carried none.
    /// </summary>
    /// <remarks>
    /// The tab hands its tick state to <c>X2ItemEnchant:Execute(selectSlotBit)</c> and the client
    /// puts it, as is, in the first extra value: a cast with socket 1 ticked arrives as
    /// <c>values=[00000001]</c> (flag 12, one value). Bit N is socket N, and nine sockets is all the
    /// detail block has room for.
    /// </remarks>
    private static uint SelectedSockets(SkillObject skillObject)
    {
        if (skillObject is not SkillObjectExtraValues extras || extras.ReadCount == 0)
            return 0;

        return (uint)extras.Values[0] & ((1u << EquipItem.SocketSlots) - 1);
    }
}
