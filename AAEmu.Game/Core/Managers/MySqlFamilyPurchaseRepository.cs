using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game.Items;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public sealed class MySqlFamilyPurchaseRepository : IFamilyPurchaseRepository
{
    private readonly IItemManager _itemManager;
    private readonly Func<MySqlConnection> _connectionFactory;

    public MySqlFamilyPurchaseRepository(IItemManager itemManager)
        : this(itemManager, MySQL.CreateConnection)
    {
    }

    internal MySqlFamilyPurchaseRepository(IItemManager itemManager, Func<MySqlConnection> connectionFactory)
    {
        _itemManager = itemManager;
        _connectionFactory = connectionFactory;
    }

    public void CommitItemConsumption(IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots)
    {
        using var connection = _connectionFactory();
        using var transaction = connection.BeginTransaction();
        try
        {
            _itemManager.PersistSnapshots(connection, transaction, itemSnapshots);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public bool TryCommitExpansion(uint familyId, uint expectedIncreaseCount, uint newIncreaseCount,
        IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots)
    {
        using var connection = _connectionFactory();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = Create(connection, transaction,
                "UPDATE families SET increased_member_count=@new_count " +
                "WHERE id=@id AND increased_member_count=@expected_count");
            command.Parameters.AddWithValue("@id", familyId);
            command.Parameters.AddWithValue("@expected_count", expectedIncreaseCount);
            command.Parameters.AddWithValue("@new_count", newIncreaseCount);
            if (command.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }

            _itemManager.PersistSnapshots(connection, transaction, itemSnapshots);
            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public bool TryCommitRename(uint familyId, string expectedName, long expectedChangeNameTime,
        string newName, long newChangeNameTime, IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots)
    {
        using var connection = _connectionFactory();
        using var transaction = connection.BeginTransaction();
        try
        {
            using var command = Create(connection, transaction,
                "UPDATE families SET name=@new_name,change_name_time=@new_time " +
                "WHERE id=@id AND name=@expected_name AND change_name_time=@expected_time");
            command.Parameters.AddWithValue("@id", familyId);
            command.Parameters.AddWithValue("@expected_name", expectedName);
            command.Parameters.AddWithValue("@expected_time", expectedChangeNameTime);
            command.Parameters.AddWithValue("@new_name", newName);
            command.Parameters.AddWithValue("@new_time", newChangeNameTime);
            if (command.ExecuteNonQuery() != 1)
            {
                transaction.Rollback();
                return false;
            }

            _itemManager.PersistSnapshots(connection, transaction, itemSnapshots);
            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static MySqlCommand Create(MySqlConnection connection, MySqlTransaction transaction, string sql) =>
        new(sql, connection, transaction);
}
