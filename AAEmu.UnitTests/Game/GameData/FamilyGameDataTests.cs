using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Families;

namespace AAEmu.UnitTests.Game.GameData;

public class FamilyGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE family_levels (id INTEGER PRIMARY KEY, level INTEGER, grade_name TEXT, exp INTEGER, buff_id INTEGER);
            CREATE TABLE family_member_limits (id INTEGER PRIMARY KEY, count INTEGER, item_id INTEGER, item_count INTEGER);
            CREATE TABLE family_roles (id INTEGER PRIMARY KEY, icon_id TEXT, role_name TEXT, role_count INTEGER);
            """);
    }

    [Test]
    public async Task Load_IndexesFamilyProgressionLimitsAndRoles()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.MaximumCountKey, 8);
        Execute(
            """
            INSERT INTO family_levels VALUES (1, 1, 'Level One', 0, 20311), (2, 2, 'Level Two', 15000, 20539), (3, 3, 'Level Three', 45000, 20540);
            INSERT INTO family_member_limits VALUES (1, 9, 48995, 1), (2, 10, 48995, 2), (3, 11, 48995, 4), (4, 12, 48995, 8);
            INSERT INTO family_roles VALUES (1, '14506', 'Owner', 1), (4, '14509', 'Member', 5);
            """);
        var data = new FamilyGameData();

        data.Load(Connection);
        data.PostLoad();

        await Assert.That(data.GetLevel(2).Exp).IsEqualTo(15000u);
        await Assert.That(data.GetEligibleLevelForExp(44999)).IsEqualTo(2u);
        await Assert.That(data.GetEligibleLevelForExp(45000)).IsEqualTo(3u);
        await Assert.That(data.MaxLevel).IsEqualTo(3u);
        await Assert.That(data.GetNextMemberLimit(FamilyContentConfig.MaximumCount).Count).IsEqualTo(9);
        await Assert.That(data.GetMemberLimit(12).ItemCount).IsEqualTo(8);
        await Assert.That(data.MaxMemberLimit).IsEqualTo(12);
        await Assert.That(data.GetRole(4).RoleCount).IsEqualTo(5);
    }

    [Test]
    public async Task AdministrationRules_UseLoadedRoleCapacityAndNextMemberLimit()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.MaximumCountKey, 8);
        Execute(
            """
            INSERT INTO family_member_limits VALUES (1, 9, 7001, 1), (2, 10, 7001, 2);
            INSERT INTO family_roles VALUES (1, '7002', 'Owner', 1), (4, '7003', 'Member', 2);
            """);
        var data = new FamilyGameData();
        data.Load(Connection);
        data.PostLoad();

        var owner = new FamilyMember { Id = 1, Role = 1 };
        var target = new FamilyMember { Id = 2, Role = 0 };
        var family = new Family { Id = 10 };
        family.AddMember(owner);
        family.AddMember(target);

        await Assert.That(FamilyProgressionRules.TryGetAssignableRole(family, target, 4, data, out var role)).IsTrue();
        await Assert.That(role.RoleCount).IsEqualTo(2);
        await Assert.That(FamilyProgressionRules.TryGetNextMemberLimit(family, data, out var next)).IsTrue();
        await Assert.That(next.Count).IsEqualTo(9);
        await Assert.That(next.ItemId).IsEqualTo(7001u);
        await Assert.That(next.ItemCount).IsEqualTo(1);

        family.AddMember(new FamilyMember { Id = 3, Role = 4 });
        family.AddMember(new FamilyMember { Id = 4, Role = 4 });
        await Assert.That(FamilyProgressionRules.TryGetAssignableRole(family, target, 4, data, out _)).IsFalse();
        await Assert.That(FamilyProgressionRules.TryGetAssignableRole(family, owner, 4, data, out _)).IsFalse();
    }

    [Test]
    public async Task Load_ReplacesPreviouslyLoadedRows()
    {
        Execute("INSERT INTO family_levels VALUES (1, 1, 'Old', 0, 1);");
        var data = new FamilyGameData();
        data.Load(Connection);

        Execute("DELETE FROM family_levels; INSERT INTO family_levels VALUES (2, 1, 'New', 0, 2);");
        data.Load(Connection);

        await Assert.That(data.GetLevel(1).GradeName).IsEqualTo("New");
        await Assert.That(data.GetLevel(1).BuffId).IsEqualTo(2u);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
