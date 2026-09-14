using AAEmu.Game.Models.Game;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class FamilyPersistenceIntegrationTests(FamilyMySqlFixture fixture) : IClassFixture<FamilyMySqlFixture>
{
    [Fact]
    public async Task Migration_SaveLoadRollbackDisbandAndRecreate_AreDurable()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_FAMILY_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        await Execute(connection, "INSERT INTO characters(id,level,heir_exp) VALUES(1,55,0),(2,56,0)");
        var family = new Family
        {
            Id = 77, Name = "Fixture", Notice = "Notice", Level = 2, Exp = 345,
            IncreasedMemberCount = 2, ResetTime = 111, ChangeNameTime = 222
        };
        family.ActSanctions[3] = 999;
        family.AddMember(new FamilyMember { Id = 1, Name = "One", Level = 55, Role = 1, Title = "Owner" });
        family.AddMember(new FamilyMember { Id = 2, Name = "Two", Level = 56, Role = 2, Title = "Member", RoleUpdateTime = 333, LoginRewardTime = 444 });
        using (var transaction = connection.BeginTransaction())
        {
            family.Save(connection, transaction);
            transaction.Commit();
            family.ConfirmSave();
        }

        var loaded = new Family { Id = family.Id };
        loaded.Load(connection);
        Assert.Equal("Fixture", loaded.Name);
        Assert.Equal(345u, loaded.Exp);
        Assert.Equal(2u, loaded.IncreasedMemberCount);
        Assert.Equal(111, loaded.ResetTime);
        Assert.Equal(333, loaded.Members.Single(x => x.Id == 2).RoleUpdateTime);
        Assert.Equal(444, loaded.Members.Single(x => x.Id == 2).LoginRewardTime);
        Assert.Equal(999, loaded.ActSanctions[3]);

        await Execute(connection,
            "INSERT INTO families(id,name) VALUES(88,'Orphan');" +
            "INSERT INTO family_members(character_id,family_id,name,role,role_update_time,title) " +
            "VALUES(1,88,'Stale',1,0,'')");
        var orphan = new Family { Id = 88 };
        orphan.Load(connection);
        Assert.Empty(orphan.Members);

        loaded.RemoveMember(loaded.Members.Single(x => x.Id == 2));
        using (var transaction = connection.BeginTransaction())
        {
            loaded.Save(connection, transaction);
            transaction.Rollback();
        }
        Assert.Equal(2L, await Scalar(connection, "SELECT COUNT(*) FROM family_members WHERE family_id=77"));

        family.RemovedMemberRejoinUntil = 777;
        foreach (var member in family.Members.ToArray()) family.RemoveMember(member);
        using (var transaction = connection.BeginTransaction())
        {
            family.Save(connection, transaction);
            transaction.Commit();
            family.ConfirmSave();
        }
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM families WHERE id=77"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM family_act_sanctions WHERE family_id=77"));
        Assert.Equal(2L, await Scalar(connection, "SELECT COUNT(*) FROM characters WHERE family=0 AND family_rejoin_until=777"));

        var recreated = new Family { Id = 77, Name = "Recreated" };
        recreated.AddMember(new FamilyMember { Id = 1, Name = "One", Level = 55, Role = 1, Title = "" });
        using (var transaction = connection.BeginTransaction())
        {
            recreated.Save(connection, transaction);
            transaction.Commit();
        }
        Assert.Equal("Recreated", await Text(connection, "SELECT name FROM families WHERE id=77"));
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> Text(MySqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
