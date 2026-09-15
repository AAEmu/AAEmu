using System.Data.Common;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// The pending inventory state that must accompany an independently durable item operation.
/// Capture and apply while holding the character state lock. Dirty flags/deletion queues are
/// deliberately retained: a rollback must remain retryable, and autosave may repeat these writes.
/// </summary>
public sealed record InventoryPersistenceSnapshot(
    uint OwnerId,
    IReadOnlyList<ItemContainer> Containers,
    IReadOnlyList<Item> Items,
    IReadOnlyList<ulong> RemovedItemIds)
{
    internal void Apply(DbConnection connection, DbTransaction transaction)
    {
        // Only delete explicitly consumed/deleted IDs, never infer deletion from an empty slot.
        foreach (var ids in RemovedItemIds.Chunk(500))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM items WHERE owner = @owner AND id IN ({string.Join(",", ids)})";
            command.AddParameter("@owner", OwnerId);
            command.ExecuteNonQuery();
        }

        foreach (var container in Containers)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "REPLACE INTO item_containers " +
                "(container_id,container_type,slot_type,container_size,owner_id,mate_id,parent_item_id) " +
                "VALUES (@id,@type,@slot_type,@size,@owner,@mate,@parent)";
            command.AddParameter("@id", container.ContainerId);
            command.AddParameter("@type", container.ContainerTypeName());
            command.AddParameter("@slot_type", (int)container.ContainerType);
            command.AddParameter("@size", container.ContainerSize);
            command.AddParameter("@owner", container.OwnerId);
            command.AddParameter("@mate", container.MateId);
            command.AddParameter("@parent", container is ItemBagContainer bag ? bag.ParentItemId : 0ul);
            command.ExecuteNonQuery();
        }

        foreach (var item in Items)
            ItemPersistence.Save(connection, transaction, item);
    }
}

internal static class ItemPersistenceCommandExtensions
{
    internal static void AddParameter(this DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
