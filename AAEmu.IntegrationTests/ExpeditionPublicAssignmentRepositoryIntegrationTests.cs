using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using AAEmu.Game.Models.Game.TodayAssignment;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class ExpeditionPublicAssignmentRepositoryIntegrationTests(ExpeditionRecruitmentMySqlFixture fixture)
    : IClassFixture<ExpeditionRecruitmentMySqlFixture>
{
    [Fact]
    public void AssignmentAndContributorUseOneTransactionAndOptimisticVersion()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        const uint expeditionId = 930001;
        var period = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
        using var connection = fixture.Open();
        Execute(connection, "INSERT INTO expeditions(id,owner,owner_name,name,mother) VALUES(930001,1,'Owner','Public Test',101)");
        var repository = new MySqlExpeditionPublicAssignmentRepository();
        var state = NewState(expeditionId, period);

        using (var transaction = connection.BeginTransaction())
        {
            Assert.True(repository.TrySave(connection, transaction, state, 0));
            repository.UpsertContributor(connection, transaction, state, 7, "Contributor", 10);
            transaction.Commit();
        }

        Assert.Equal(1u, ScalarUInt(connection,
            "SELECT version FROM expedition_public_assignments WHERE expedition_id=930001"));
        Assert.Equal(10u, ScalarUInt(connection,
            "SELECT contribution FROM expedition_public_assignment_contributors WHERE expedition_id=930001 AND character_id=7"));

        // Simulate a later progress event whose transaction fails after both writes were staged.
        var progress = state.Copy();
        progress.Objectives[0] = 20;
        using (var transaction = connection.BeginTransaction())
        {
            repository.UpsertContributor(connection, transaction, progress, 7, "Contributor", 5);
            Assert.True(repository.TrySave(connection, transaction, progress, 1));
            transaction.Rollback();
        }

        Assert.Equal(1u, ScalarUInt(connection,
            "SELECT version FROM expedition_public_assignments WHERE expedition_id=930001"));
        Assert.Equal(10u, ScalarUInt(connection,
            "SELECT contribution FROM expedition_public_assignment_contributors WHERE expedition_id=930001 AND character_id=7"));

        // A stale create/update cannot replace the already committed selection.
        using var staleTransaction = connection.BeginTransaction();
        Assert.False(repository.TrySave(connection, staleTransaction, NewState(expeditionId, period), 0));
        staleTransaction.Rollback();
    }

    private static ExpeditionPublicAssignmentState NewState(uint expeditionId, DateTime period) => new()
    {
        ExpeditionId = expeditionId,
        PeriodStart = period,
        RealStep = 13,
        GroupId = 172,
        QuestContextId = 10887,
        Status = TodayAssignmentStatus.Progress
    };

    private static uint ScalarUInt(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToUInt32(command.ExecuteScalar());
    }

    private static void Execute(MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
