using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class ExpeditionRecruitmentPersistenceIntegrationTests(ExpeditionRecruitmentMySqlFixture fixture)
    : IClassFixture<ExpeditionRecruitmentMySqlFixture>
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public void RecruitmentAndApplication_RollbackLeavesNoRows()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed();
        var repository = new MySqlExpeditionRecruitmentRepository(fixture);
        using (var connection = fixture.Open())
        using (var transaction = connection.BeginTransaction())
        {
            repository.Upsert(new(1, 1, "rollback", Now, Now.AddDays(3)), connection, transaction);
            repository.AddApplication(new(1, 10, "memo", Now), connection, transaction);
            transaction.Rollback();
        }
        Assert.Empty(repository.GetActive(Now));
        Assert.Empty(repository.GetApplicationsForCharacter(10, Now));
    }

    [Fact]
    public async Task CharacterRowLockSerializesCrossGuildApplicationCap()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed();
        var repository = new MySqlExpeditionRecruitmentRepository(fixture);
        using (var connection = fixture.Open())
        using (var transaction = connection.BeginTransaction())
        {
            foreach (var id in Enumerable.Range(1, 5))
            {
                repository.Upsert(new((uint)id, 1, "open", Now, Now.AddDays(3)), connection, transaction);
                if (id < 5) repository.AddApplication(new((uint)id, 10, "memo", Now), connection, transaction);
            }
            transaction.Commit();
        }
        using var first = fixture.Open();
        using var firstTx = first.BeginTransaction();
        repository.GetCandidateForUpdate(10, first, firstTx);
        Assert.Equal(4, repository.CountApplicationsForUpdate(10, Now, first, firstTx));
        var competing = Task.Run(() =>
        {
            using var second = fixture.Open();
            using var secondTx = second.BeginTransaction();
            repository.GetCandidateForUpdate(10, second, secondTx);
            var count = repository.CountApplicationsForUpdate(10, Now, second, secondTx);
            secondTx.Rollback();
            return count;
        });
        await Task.Delay(100);
        Assert.False(competing.IsCompleted);
        repository.AddApplication(new(5, 10, "fifth", Now), first, firstTx);
        firstTx.Commit();
        Assert.Equal(5, await competing.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ParentReplacePersistence_DoesNotDeleteRecruitmentOrApplications()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed();
        var repository = new MySqlExpeditionRecruitmentRepository(fixture);
        using var connection = fixture.Open();
        using (var transaction = connection.BeginTransaction())
        {
            repository.Upsert(new(1, 1, "survives", Now, Now.AddDays(3)), connection, transaction);
            repository.AddApplication(new(1, 10, "survives", Now), connection, transaction);
            transaction.Commit();
        }
        using (var replace = connection.CreateCommand())
        {
            replace.CommandText = "REPLACE INTO expeditions (id) VALUES(1)";
            replace.ExecuteNonQuery();
            replace.CommandText = "REPLACE INTO characters (id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id,family,money,expedition_rejoin_until) VALUES(10,1,'Applicant',55,0,1,2,3,4,0,0,0,0)";
            replace.ExecuteNonQuery();
        }
        Assert.Single(repository.GetActive(Now));
        Assert.Single(repository.GetApplicationsForCharacter(10, Now));
    }

    [Fact]
    public void RegistrationFeeAndRecruitment_CommitAndRollbackTogether()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed();
        var repository = new MySqlExpeditionRecruitmentRepository(fixture);
        using var connection = fixture.Open();
        using (var fund = connection.CreateCommand())
        {
            fund.CommandText = "UPDATE characters SET money=250000 WHERE id=10";
            fund.ExecuteNonQuery();
        }

        using (var transaction = connection.BeginTransaction())
        {
            repository.Upsert(new(1, 1, "rollback", Now, Now.AddDays(3)), connection, transaction);
            Assert.True(repository.TryDebitMoney(10, 100000, connection, transaction));
            transaction.Rollback();
        }
        Assert.Equal(250000, Scalar(connection, "SELECT money FROM characters WHERE id=10"));
        Assert.Empty(repository.GetActive(Now));

        using (var transaction = connection.BeginTransaction())
        {
            repository.Upsert(new(1, 1, "commit", Now, Now.AddDays(3)), connection, transaction);
            Assert.True(repository.TryDebitMoney(10, 100000, connection, transaction));
            transaction.Commit();
        }
        Assert.Equal(150000, Scalar(connection, "SELECT money FROM characters WHERE id=10"));
        Assert.Single(repository.GetActive(Now));
    }

    [Fact]
    public void RegistrationFee_ConditionalDebitRejectsInsufficientDatabaseBalance()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed();
        var repository = new MySqlExpeditionRecruitmentRepository(fixture);
        using var connection = fixture.Open();
        using var transaction = connection.BeginTransaction();
        Assert.Throws<ArgumentOutOfRangeException>(() => repository.TryDebitMoney(10, -1, connection, transaction));
        Assert.False(repository.TryDebitMoney(10, 1, connection, transaction));
        transaction.Rollback();
        Assert.Equal(0, Scalar(connection, "SELECT money FROM characters WHERE id=10"));
    }

    [Fact]
    public void DeletedCharacter_IsNotReturnedAsRecruitmentCandidate()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Seed();
        var repository = new MySqlExpeditionRecruitmentRepository(fixture);
        using var connection = fixture.Open();
        using (var markDeleted = connection.CreateCommand())
        {
            markDeleted.CommandText = "UPDATE characters SET deleted=1 WHERE id=10";
            markDeleted.ExecuteNonQuery();
        }
        Assert.Null(repository.GetCandidate(10));
        Assert.Empty(repository.GetCandidates(new uint[] { 10 }));
    }

    private static long Scalar(MySql.Data.MySqlClient.MySqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void Seed()
    {
        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM expedition_recruitment_applications; DELETE FROM expedition_recruitments; DELETE FROM characters; DELETE FROM expeditions";
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO characters
                (id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id,family,money,expedition_rejoin_until)
            VALUES(10,1,'Applicant',55,0,1,2,3,4,0,0,0,0)
            """;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO expeditions (id) VALUES(1),(2),(3),(4),(5)";
        command.ExecuteNonQuery();
    }
}
