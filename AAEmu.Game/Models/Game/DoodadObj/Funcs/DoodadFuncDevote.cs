using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Contribution ("devote") interaction used by the Auroria faction bases, walls, bridges and
/// purification monoliths. Each use consumes <see cref="ItemCount"/> of <see cref="ItemId"/> and
/// raises the doodad's contribution counter by one. Once the counter reaches <see cref="Count"/>
/// the doodad advances to the func's next phase.
/// </summary>
public class DoodadFuncDevote : DoodadFuncTemplate
{
    // doodad_func_devotes
    /// <summary>Number of contributions required to advance to the next phase</summary>
    public int Count { get; set; }
    /// <summary>Item consumed per contribution</summary>
    public uint ItemId { get; set; }
    /// <summary>Amount of <see cref="ItemId"/> consumed per contribution</summary>
    public int ItemCount { get; set; }
    public string TooltipText { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncDevote: ItemId {0}, ItemCount {1}, Count {2}, ObjId {3}", ItemId, ItemCount, Count, owner.ObjId);

        owner.ToNextPhase = false;

        if (caster is not Character character)
            return;

        // ConsumeItem happily consumes a partial amount, so verify the full cost is available first
        if (ItemId > 0 && ItemCount > 0)
        {
            if (character.Inventory.GetItemsCount(ItemId) < ItemCount)
            {
                Logger.Debug($"DoodadFuncDevote: {character.Name} lacks {ItemCount}x item {ItemId} for doodad {owner.TemplateId} (objId {owner.ObjId})");
                return;
            }

            var consumed = character.Inventory.Bag.ConsumeItem(ItemTaskType.DoodadItemChanger, ItemId, ItemCount, null);
            if (consumed < ItemCount)
            {
                Logger.Warn($"DoodadFuncDevote: consumed only {consumed}/{ItemCount} of item {ItemId} for doodad {owner.TemplateId} (objId {owner.ObjId})");
                return;
            }
        }

        var contributions = owner.Data + 1;

        // Count <= 0 would mean a single contribution completes it
        if (contributions < Count)
        {
            Logger.Debug($"DoodadFuncDevote: doodad {owner.TemplateId} (objId {owner.ObjId}) at {contributions}/{Count}, {Count - contributions} left");
            PublishProgress(owner, contributions);
            return;
        }

        // Target reached - reset so the counter is clean for whatever the next phase requires
        Logger.Info($"DoodadFuncDevote: doodad {owner.TemplateId} (objId {owner.ObjId}) reached {Count} contributions, advancing to phase {nextPhase}");
        PublishProgress(owner, 0);
        owner.ToNextPhase = true;
    }

    /// <summary>
    /// Stores the contribution counter and pushes it to nearby clients. The client already knows
    /// the required total from its own copy of doodad_func_devotes and renders the remaining
    /// amount, so Data carries the number of contributions made so far.
    /// </summary>
    /// <remarks>
    /// Data is the doodad's generic per-instance int: it is serialized in Doodad.Write, persisted to
    /// the doodads.data column, and its setter saves automatically when the doodad IsPersistent.
    /// Using it as the counter means construction progress survives a restart with no extra storage.
    /// Other doodad kinds (coffers) use Data for their own purpose, but a doodad is never both.
    /// </remarks>
    public static void PublishProgress(Doodad owner, int contributions)
    {
        owner.Data = contributions;
        owner.BroadcastPacket(new SCDoodadChangedPacket(owner.ObjId, owner.Data), true);
    }
}
