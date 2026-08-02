using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// One sowable option on a farm plot: the item it costs, how much of it, the skill that selects it and the
/// phase the plot becomes once it is sown.
///
/// A plot carries one of these per crop it accepts, in phase order — group 24993 lists potato, cucumber,
/// carrot then onion — and <see cref="Skills.Effects.DoodadItemChangeEffect"/> picks between them by position.
/// </summary>
public class DoodadFuncItemChanger : DoodadPhaseFuncTemplate
{
    public int NextPhase { get; set; }
    public uint ItemId { get; set; }
    public int ItemCount { get; set; }
    public uint SkillId { get; set; }

    /// <summary>
    /// Takes the seed and moves the plot on to the crop phase. Returns false when the caster cannot pay, so
    /// the plot is left as it was rather than growing something nobody was charged for.
    /// </summary>
    public bool Apply(BaseUnit caster, Doodad owner)
    {
        if (caster is not Character character)
            return false;

        lock (owner)
            return ApplyLocked(character, owner);
    }

    private bool ApplyLocked(Character character, Doodad owner)
    {
        if (!owner.CurrentPhaseFuncs.Any(func =>
                func.FuncId == Id && func.FuncType == nameof(DoodadFuncItemChanger)))
            return false;

        if (ItemId > 0 && ItemCount > 0)
        {
            if (character.Inventory.GetItemsCount(SlotType.Inventory, ItemId) < ItemCount)
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
                return false;
            }

            // ConsumeItem reports how much it actually took; a short take must not still plant the crop.
            if (character.Inventory.ConsumeItem([SlotType.Inventory], ItemTaskType.DoodadCreate, ItemId, ItemCount, null) < ItemCount)
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
                return false;
            }
        }

        if (NextPhase > 0)
            owner.DoChangePhase(character, NextPhase);

        return true;
    }

    /// <summary>
    /// Reached when the plot's phase is entered rather than when a crop is chosen; the choice is driven by the
    /// skill's effect, which calls <see cref="Apply"/> with the index it names.
    /// </summary>
    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace($"DoodadFuncItemChanger: ItemId {ItemId} x{ItemCount}, SkillId {SkillId}, NextPhase {NextPhase}");
        return false;
    }
}
