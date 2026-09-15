using AAEmu.Game.Core.Managers;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Trading;

public sealed class MySqlSpecialtySaleStore(
    ISaveManager saveManager,
    ISpecialtyMarketStore marketStore,
    IMailManager mailManager) : ISpecialtySaleStore
{
    public SpecialtySaleCommitResult Commit(SpecialtySaleWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Market);

        try
        {
            return saveManager.ExecuteOperation((connection, transaction) =>
            {
                marketStore.Apply(connection, transaction, write.Market);
                DeleteClaimedPack(connection, transaction, write);
                UpdateLabor(connection, transaction, write);
                mailManager.PersistPreparedBatch(write.PayoutMails, connection, transaction);

                return SpecialtySaleCommitResult.Committed;
            });
        }
        catch (SpecialtySaleConflictException exception)
        {
            return exception.Result;
        }
        catch (SpecialtyMarketConflictException)
        {
            return SpecialtySaleCommitResult.MarketConflict;
        }
    }

    private static void DeleteClaimedPack(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SpecialtySaleWrite write)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM items WHERE id = @id AND template_id = @template_id AND owner = @owner " +
            "AND container_id = @container_id AND slot_type = @slot_type AND slot = @slot AND count = @count";
        command.Parameters.AddWithValue("@id", write.PackItemId);
        command.Parameters.AddWithValue("@template_id", write.PackTemplateId);
        command.Parameters.AddWithValue("@owner", write.PackOwnerId);
        command.Parameters.AddWithValue("@container_id", write.PackContainerId);
        command.Parameters.AddWithValue("@slot_type", (int)write.PackSlotType);
        command.Parameters.AddWithValue("@slot", write.PackSlot);
        command.Parameters.AddWithValue("@count", write.PackCount);
        command.Prepare();

        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtySaleConflictException(SpecialtySaleCommitResult.PackNotPersisted);
    }

    private static void UpdateLabor(
        MySqlConnection connection,
        MySqlTransaction transaction,
        SpecialtySaleWrite write)
    {
        if (write.ExpectedLabor == write.NewLabor && write.ExpectedLocalLabor == write.NewLocalLabor)
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE accounts SET labor = @new_labor, local_labor = @new_local_labor " +
            "WHERE account_id = @account_id AND labor = @expected_labor AND local_labor = @expected_local_labor";
        command.Parameters.AddWithValue("@new_labor", write.NewLabor);
        command.Parameters.AddWithValue("@new_local_labor", write.NewLocalLabor);
        command.Parameters.AddWithValue("@account_id", write.AccountId);
        command.Parameters.AddWithValue("@expected_labor", write.ExpectedLabor);
        command.Parameters.AddWithValue("@expected_local_labor", write.ExpectedLocalLabor);
        command.Prepare();

        if (command.ExecuteNonQuery() != 1)
            throw new SpecialtySaleConflictException(SpecialtySaleCommitResult.LaborConflict);
    }

}

internal sealed class SpecialtySaleConflictException(SpecialtySaleCommitResult result) : Exception
{
    public SpecialtySaleCommitResult Result { get; } = result;
}
