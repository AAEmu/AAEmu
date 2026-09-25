using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Families;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class FamilyAdministrationManagerTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE family_levels (id INTEGER PRIMARY KEY, level INTEGER, grade_name TEXT, exp INTEGER, buff_id INTEGER);
            CREATE TABLE family_member_limits (id INTEGER PRIMARY KEY, count INTEGER, item_id INTEGER, item_count INTEGER);
            CREATE TABLE family_roles (id INTEGER PRIMARY KEY, icon_id TEXT, role_name TEXT, role_count INTEGER);
            INSERT INTO family_member_limits VALUES (1, 9, 7001, 1);
            INSERT INTO family_roles VALUES (1, '7002', 'Owner', 1), (4, '7003', 'Member', 2);
            """);
    }

    [Test]
    public async Task ManagerUsesInjectedCatalogForRoleAndNextCapacity()
    {
        ContentConfigGameData.Instance.SetForTest(FamilyContentConfig.MaximumCountKey, 8);
        var catalog = new FamilyGameData();
        catalog.Load(Connection);
        catalog.PostLoad();

        var owner = NewCharacter(1, 10, "Owner");
        var member = NewCharacter(2, 10, "Member");
        var family = new Family { Id = 10 };
        family.AddMember(new FamilyMember { Id = owner.Id, Character = owner, Name = owner.Name, Role = 1 });
        family.AddMember(new FamilyMember { Id = member.Id, Character = member, Name = member.Name, Role = 0 });

        var saves = 0;
        var purchases = Mock.Of<IFamilyPurchaseService>();
        purchases.Expand(Any<Character>(), Any<Family>(), Any<uint>(), Any<int>())
            .Returns((Character _, Family target, uint _, int _) =>
            {
                target.IncreasedMemberCount++;
                return new FamilyPurchaseResult(true, FamilyPurchaseFailure.None);
            });
        var manager = new FamilyManager(Mock.Of<IWorldManager>().Object, Mock.Of<IChatManager>().Object,
            Mock.Of<IFamilyIdManager>().Object, purchases.Object, _ => saves++, () => 100,
            _ => true, catalog);
        SetField(manager, "_families", new Dictionary<uint, Family> { [family.Id] = family });
        SetField(manager, "_familyMembers", family.Members.ToDictionary(x => x.Id));

        manager.ChangeMemberRole(owner, member.Id, 4);
        manager.IncreaseMemberLimit(owner);

        await Assert.That(family.GetMember(member)?.Role).IsEqualTo((byte)4);
        await Assert.That(family.GetMember(member)?.RoleUpdateTime).IsEqualTo(100L);
        await Assert.That(family.IncreasedMemberCount).IsEqualTo(1u);
        await Assert.That(saves).IsEqualTo(1);
        purchases.Expand(owner, family, 7001, 1).WasCalled(Times.Once);
    }

    private static Character NewCharacter(uint id, uint family, string name) =>
        new(new UnitCustomModelParams()) { Id = id, Family = family, Name = name };

    private static void SetField<T>(FamilyManager manager, string name, T value)
    {
        typeof(FamilyManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, value);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
