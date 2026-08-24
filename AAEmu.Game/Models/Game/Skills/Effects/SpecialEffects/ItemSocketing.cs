using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ItemSocketing : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemSocketing;

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
        // TODO ...
        Logger.Debug("Special effects: ItemSocketing value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (caster is not Character owner)
        {
            Logger.Error($"Special effects: ItemSocketing caster {caster.Id} is not a character");
            return;
        }

        if (casterObj is not SkillItem gemSkillItem)
        {
            Logger.Error($"Special effects: ItemSocketing casterObj {casterObj} is not a SkillItem");
            return;
        }

        if (targetObj is not SkillCastItemTarget skillTargetItem)
        {
            Logger.Error($"Special effects: ItemSocketing targetObj {targetObj} is not a SkillCastItemTarget");
            return;
        }

        var targetItem = owner.Inventory.GetItemById(skillTargetItem.Id);
        var gemItem = owner.Inventory.GetItemById(gemSkillItem.ItemId);

        if (targetItem is null || gemItem is null)
        {
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} or gemItem {gemSkillItem.ItemId} not found");
            return;
        }

        if (targetItem is not EquipItem equipItem)
        {
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} was not a EquipItem");
            return;
        }

        byte result = 0;
        var installed = false;
        if (gemItem.TemplateId != Item.DawnStone)
        {
            // Add LunaGem
            var gemCount = 0u;
            foreach (var gem in equipItem.GemIds)
            {
                if (gem != 0)
                {
                    gemCount++;
                }
            }

            // A piece only has so many sockets, and how many follows its slot and its grade. The
            // window greys its own button when the run is full, so this only catches a stale window
            // or a hand-built request.
            var slotTypeId = equipItem.Template switch
            {
                WeaponTemplate weapon => weapon.HoldableTemplate?.SlotTypeId ?? 0,
                ArmorTemplate armor => armor.SlotTemplate?.SlotTypeId ?? 0,
                AccessoryTemplate accessory => accessory.SlotTemplate?.SlotTypeId ?? 0,
                _ => 0u
            };

            if (gemCount >= ItemManager.Instance.GetSocketNumLimit(slotTypeId, equipItem.Grade))
            {
                owner.SendErrorMessage(ErrorMessageType.ItemCannotUse);
                return;
            }

            // Formula 38 prices the attempt: it climbs with the gear's level, the gem's own level and
            // how many sockets are already filled. Charged before the roll, so a player who cannot
            // cover it keeps both their coin and their gem - and a failure that clears the item is
            // still paid for, the same way a failed regrade is.
            var socketCost = SocketingCost(equipItem, gemItem, gemCount);
            if (socketCost > 0)
            {
                if (owner.Money < socketCost)
                {
                    owner.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                    return;
                }

                owner.SubtractMoney(SlotType.Inventory, socketCost);
            }

            // Roll for Success
            var gemRoll = Random.Shared.Next(0, 10000);
            var gemChance = ItemManager.Instance.GetSocketChance(gemItem.TemplateId, gemCount);
            // var gemChance = int.MaxValue; //gives 100% success rates

            if (gemRoll < gemChance)
            {
                // Success
                equipItem.GemIds[gemCount] = gemItem.TemplateId;
                result = 1;
            }
            else if (ItemManager.Instance.GetSocketFailBreaks(gemItem.TemplateId))
            {
                // Only the rows that say so take the rest of the piece's gems down with the attempt.
                for (var i = 0; i < equipItem.GemIds.Length; i++)
                {
                    equipItem.GemIds[i] = 0;
                }
            }
            installed = true;
        }
        else
        {
            // DawnStone
            for (var i = 0; i < equipItem.GemIds.Length; i++)
            {
                equipItem.GemIds[i] = 0;
            }
            result = 1;
        }

        equipItem.IsDirty = true;

        // The piece goes out on the detail packet and nowhere else. Handing the same blob to the
        // window a second time as a task item is what leaves it drawn as broken: the window now
        // learns the cast is over from the skill's own reagent task and re-reads the piece out of
        // the bag itself, so it no longer needs to be told. Tempering has always published this way
        // and its piece survives; socketing did not, and its piece did not.
        owner.SendPacket(new SCItemDetailUpdatedPacket(equipItem));

        // Gear bonuses are totalled when a piece is equipped and not again, so a change made to a
        // piece already being worn never reached the character sheet. Re-total here; the piece is
        // only in the sum at all while it sits in the equipment container.
        if (equipItem.SlotType == SlotType.Equipment)
            owner.UpdateGearBonuses(null, null);

        // kind separates seating a gem from clearing the piece; success is the outcome the window
        // waits on before it will take another attempt.
        owner.SendPacket(new SCItemSocketingLunagemResultPacket(result, equipItem.Id,
            gemItem.TemplateId, (byte)(installed ? 1 : 0), result != 0));

        // The tab keeps a tally of the run the player asked for and takes no further press while it
        // stands above zero. A successful result takes one off it; only an unsuccessful one clears
        // it outright. One cast seats one gem, so a press for several would leave the rest of that
        // tally hanging and the tab shut - closing the run off here hands it straight back.
        var requested = RequestedAttempts(skillObject);
        if (requested > 1 && result != 0)
        {
            owner.SendPacket(new SCItemSocketingLunagemResultPacket(0, equipItem.Id,
                gemItem.TemplateId, (byte)(installed ? 1 : 0), false));
        }

        owner.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);
    }

    /// <summary>
    /// How many gems the tab asked to seat with one press.
    /// </summary>
    /// <remarks>
    /// It sends its spinner along in the cast's extra values - a byte for "all", then the count as
    /// an int, so a press for three arrives as 0x00000300 in the first of the thirteen. Casts logged
    /// from the tab carry one through six there and nothing else, which is the range it offers.
    /// </remarks>
    private static int RequestedAttempts(SkillObject skillObject)
    {
        if (skillObject is not SkillObjectExtraValues extras || extras.Values.Length == 0)
            return 1;

        var requested = (extras.Values[0] >> 8) & 0xFF;
        return requested < 1 ? 1 : requested;
    }

    /// <summary>
    /// Evaluates <c>item_socketing_cost</c> (formula 38) for the socket being filled.
    /// </summary>
    /// <remarks>
    /// Every input is table-supplied: both levels come off the templates and the count comes off the
    /// item. No shipped table carries <c>item_socketing_cost_mul</c>, which leaves it neutral.
    /// </remarks>
    private static long SocketingCost(EquipItem equipItem, Item gemItem, uint usedSockets)
    {
        var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.ItemSocketingCost);
        if (formula == null)
            return 0;

        var parameters = new Dictionary<string, double>
        {
            { "item_level", equipItem.Template?.Level ?? 0 },
            { "socket_item_level", gemItem.Template?.Level ?? 0 },
            { "item_used_socket", usedSockets },
            { "item_socketing_cost_mul", 0 }
        };

        var cost = formula.Evaluate(parameters);
        if (double.IsNaN(cost) || cost <= 0)
            return 0;

        return cost > long.MaxValue ? long.MaxValue : (long)cost;
    }
}
