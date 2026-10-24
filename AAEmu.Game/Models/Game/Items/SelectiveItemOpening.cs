using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using NLog;

namespace AAEmu.Game.Models.Game.Items;

internal static class SelectiveItemOpening
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    internal static bool TryBuildRewards(SelectiveItemEffectTemplate effect, uint attempts,
        IReadOnlyList<uint> selected, out int burn, out List<ItemAcquisitionRequest> rewards)
    {
        burn = 0;
        rewards = [];
        if (effect == null || attempts == 0 || attempts > int.MaxValue || selected == null ||
            selected.Count == 0 || selected.Count > Math.Max(1, effect.SelectCount) ||
            (!effect.IsMulti && selected.Count != 1 && selected.Count != Math.Max(1, effect.SelectCount)) ||
            selected.Distinct().Count() != selected.Count)
            return false;
        try
        {
            burn = checked(Math.Max(1, effect.ConsumeItemCount) * (int)attempts);
            foreach (var index in selected)
            {
                if (index == 0 || index > effect.Elems.Count)
                    return false;
                var elem = effect.Elems[(int)index - 1];
                rewards.Add(new ItemAcquisitionRequest(elem.ItemId,
                    checked(Math.Max(1, elem.Count) * (int)attempts), elem.GradeId));
            }
            // Different choices may grant the same item. Check their combined count too.
            rewards = rewards.GroupBy(reward => (reward.TemplateId, reward.Grade))
                .Select(group => new ItemAcquisitionRequest(group.Key.TemplateId,
                    checked(group.Sum(reward => reward.Count)), group.Key.Grade)).ToList();
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    internal static ErrorMessageType? Open(Character character, ulong sourceId,
        SelectiveItemEffectTemplate effect, uint attempts, IReadOnlyList<uint> selected,
        IItemManager items, Action<IReadOnlyList<ItemPersistenceSnapshot>> persist = null)
    {
        if (!TryBuildRewards(effect, attempts, selected, out var burn, out var rewards))
            return ErrorMessageType.ItemCannotUse;

        ItemConsumptionPublication debit;
        ItemAcquisitionPublication credit;
        using (PersistenceOperationScope.Enter())
        lock (character.Inventory.MutationSyncRoot)
        {
            var inventory = character.Inventory;
            var source = inventory.Bag.GetItemByItemId(sourceId);
            if (source?.Template?.UseSkillId != effect.SkillId ||
                !inventory.TryPlanExactBagConsumption(sourceId, burn, out var consumption))
                return ErrorMessageType.NotEnoughItem;
            if (!inventory.TryPlanBagAcquisition(items, rewards, DateTime.UtcNow, out var acquisition, consumption))
                return ErrorMessageType.BagFull;
            using (acquisition)
            {
                var snapshots = consumption.CapturePersistenceSnapshots(items)
                    .Concat(acquisition.CapturePersistenceSnapshots()).ToArray();
                try
                {
                    (persist ?? Persist)(snapshots);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Selective opening failed for character {0}; no items changed", character.Id);
                    return ErrorMessageType.ItemCannotUse;
                }
                acquisition.MarkCommitted();
                debit = consumption.ApplyCommitted(ItemTaskType.ConsumeSkillSource);
                credit = acquisition.ApplyCommitted(ItemTaskType.SkillEffectGainItem);
                debit.PublishPackets();
                credit.PublishPackets();
            }
        }
        // A failing quest observer cannot turn an already committed opening into a reported failure.
        try { debit.PublishCallbacks(); }
        catch (Exception exception) { Logger.Error(exception, "Selective opening debit callbacks failed for {0}", character.Id); }
        try { credit.PublishCallbacks(); }
        catch (Exception exception) { Logger.Error(exception, "Selective opening credit callbacks failed for {0}", character.Id); }
        Logger.Info("Selective opening: character={0} source={1} attempts={2} consumed={3}",
            character.Id, sourceId, attempts, burn);
        return null;
    }

    private static void Persist(IReadOnlyList<ItemPersistenceSnapshot> snapshots)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        ItemManager.Instance.PersistSnapshots(connection, transaction, snapshots);
        transaction.Commit();
    }
}
