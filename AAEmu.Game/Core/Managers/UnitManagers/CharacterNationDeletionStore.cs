using System.Data.Common;

namespace AAEmu.Game.Core.Managers.UnitManagers;

/// <summary>
/// Stages character-carried faction and leadership cleanup in the character deletion transaction.
/// The final conditional character delete decides whether the transaction commits; a stale/cancelled delete
/// therefore restores every row changed here.
/// </summary>
public static class CharacterNationDeletionStore
{
    public static void Stage(DbConnection connection, DbTransaction transaction, uint characterId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        Execute(connection, transaction, """
            UPDATE characters SET
                faction_id=0, faction_name='',
                leadership_point=0, leadership_period_point=0, accumulated_leadership_point=0,
                daily_leadership_point=0, last_daily_leadership_point_time='1970-01-01 00:00:00',
                mobilization_order_today_count=0, mobilization_order_total_count=0,
                last_mobilization_order_time='1970-01-01 00:00:00',
                last_mobilization_accept_time='1970-01-01 00:00:00',
                last_mobilization_not_recv_time='1970-01-01 00:00:00'
            WHERE id=@characterId AND deleted=0
            """, characterId);

        Execute(connection, transaction,
            "DELETE FROM faction_relation_counts WHERE character_id=@characterId OR other_id=@characterId",
            characterId);
    }

    private static void Execute(DbConnection connection, DbTransaction transaction, string sql, uint characterId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@characterId";
        parameter.Value = characterId;
        command.Parameters.Add(parameter);
        command.ExecuteNonQuery();
    }
}
