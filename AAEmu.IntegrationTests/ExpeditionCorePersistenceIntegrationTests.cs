using System.Reflection;
using System.Runtime.CompilerServices;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace AAEmu.IntegrationTests;

[Collection(ExpeditionCoreStaticCollection.Name)]
public sealed class ExpeditionCorePersistenceIntegrationTests(ExpeditionRecruitmentMySqlFixture fixture)
    : IClassFixture<ExpeditionRecruitmentMySqlFixture>
{
    [Fact]
    public void ContributionMutation_PersistsScopedBalancesAndWeeklyPeriod()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id) VALUES(1); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo,contribution_point,weekly_contribution_point) VALUES(10,1,'Member',55,0,NOW(),1,2,3,'',100,20);");
        var (manager, character, member) = Context();

        Assert.True(manager.TryChangeContributionPoints(character, 25, true));

        Assert.Equal(125, Scalar("SELECT contribution_point FROM expedition_members WHERE character_id=10"));
        Assert.Equal(25, Scalar("SELECT weekly_contribution_point FROM expedition_members WHERE character_id=10"));
        Assert.Equal(125u, member.ContributionPoint);
        Assert.Equal(25u, member.WeeklyContributionPoint);
    }

    [Fact]
    public void StagedExp_RequiresCallerGuardsAndNeverReducesLegacyOverCapExp()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,exp,level,daily_exp) VALUES(1,150,1,0); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Member',55,0,NOW(),1,2,3,'');");
        var (manager, character, _) = Context();
        character.Expedition.Exp = 150;
        ExpeditionLevelGameData.Instance.SetForTest(new ExpeditionLevel { Id = 1, TotalExp = 100 });
        using var connection = fixture.Open();
        using var transaction = connection.BeginTransaction();

        Assert.Throws<InvalidOperationException>(() =>
            manager.TryStageExp(character.Expedition, 25, connection, transaction, out _));
        ExpeditionManager.ExpeditionExpCommit staged;
        using (PersistenceOperationScope.Enter())
        lock (character.Expedition.SyncRoot)
        {
            Assert.True(manager.TryStageExp(character.Expedition, 25, connection, transaction, out staged));
            Assert.Equal(0u, staged.AppliedAmount);
            transaction.Commit();
            staged.Apply();
        }
        staged.Publish();

        Assert.Equal(150, Scalar("SELECT exp FROM expeditions WHERE id=1"));
        Assert.Equal(150u, character.Expedition.Exp);
    }

    [Fact]
    public void CreateExpedition_CommitsFeeFiveClaimsAndRosterTogether()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        var (manager, owner, members) = CreationContext();

        manager.CreateExpedition("Guild Test", FactionsEnum.Nuian, owner.Connection);

        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expeditions WHERE id=50"));
        Assert.Equal(5, Scalar("SELECT COUNT(*) FROM expedition_members WHERE expedition_id=50"));
        Assert.Equal(5, Scalar("SELECT COUNT(*) FROM characters WHERE expedition_id=50"));
        Assert.Equal(500, Scalar("SELECT money FROM characters WHERE id=101"));
        Assert.All(members, member => Assert.Equal((FactionsEnum)50, member.Expedition.Id));
    }

    [Fact]
    public void CreateExpedition_RosterFailureRollsBackFeeClaimsGuildAndMemory()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        var (manager, owner, members) = CreationContext();
        var cost = AppConfiguration.Instance.Expedition.Create.Cost;
        Execute("CREATE TRIGGER fail_founder_roster BEFORE INSERT ON expedition_members FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced founder rollback';");

        Assert.ThrowsAny<Exception>(() => manager.CreateExpedition("Guild Test", FactionsEnum.Nuian, owner.Connection));

        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expeditions"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_members"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM characters WHERE expedition_id<>0"));
        Assert.Equal(cost + 500, Scalar("SELECT money FROM characters WHERE id=101"));
        Assert.All(members, member => Assert.Null(member.Expedition));
    }

    [Fact]
    public void LeaveFailure_RollsBackDatabaseAndRestoresRosterAndCharacterReference()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner) VALUES(1,99); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Member',55,0,NOW(),1,2,3,''); CREATE TRIGGER fail_expedition_save BEFORE UPDATE ON expeditions FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced rollback';");
        var (manager, character, member) = Context();

        Assert.ThrowsAny<Exception>(() => manager.Leave(character));

        Assert.Equal(1, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_members WHERE character_id=10"));
        Assert.NotNull(character.Expedition);
        Assert.Contains(member, character.Expedition.Members);
    }

    [Fact]
    public void Leave_CommitsScopedRosterRemovalAndCharacterCooldown()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner) VALUES(1,99); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Member',55,0,NOW(),1,2,3,'');");
        var (manager, character, _) = Context();

        manager.Leave(character);

        Assert.Equal(0, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.True(Scalar("SELECT expedition_rejoin_until FROM characters WHERE id=10") > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_members WHERE character_id=10"));
        Assert.Null(character.Expedition);
    }

    [Fact]
    public void DailyContribution_PersistsCounterMemberAndDescriptorInOneTransaction()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id) VALUES(1); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo,contribution_point,weekly_contribution_point) VALUES(10,1,'Member',55,0,NOW(),1,2,3,'',100,0);");
        var (manager, character, member) = Context();

        Assert.True(manager.TryAddDailyContributionPoints(character, 30));

        Assert.Equal(130, Scalar("SELECT contribution_point FROM expedition_members WHERE character_id=10"));
        Assert.Equal(30, Scalar("SELECT contribution_used FROM expedition_daily_activity WHERE character_id=10"));
        Assert.Equal(30, Scalar("SELECT daily_contribution_point FROM expeditions WHERE id=1"));
        Assert.Equal(130u, member.ContributionPoint);
        Assert.Equal(30u, character.Expedition.DailyContributionPoint);
    }

    [Fact]
    public void DailyContribution_DescriptorFailureRollsBackEveryWriteAndMemory()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id) VALUES(1); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo,contribution_point,weekly_contribution_point) VALUES(10,1,'Member',55,0,NOW(),1,2,3,'',100,0); CREATE TRIGGER fail_expedition_save BEFORE UPDATE ON expeditions FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced rollback';");
        var (manager, character, member) = Context();

        Assert.ThrowsAny<Exception>(() => manager.TryAddDailyContributionPoints(character, 30));

        Assert.Equal(100, Scalar("SELECT contribution_point FROM expedition_members WHERE character_id=10"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_daily_activity"));
        Assert.Equal(0, Scalar("SELECT daily_contribution_point FROM expeditions WHERE id=1"));
        Assert.Equal(100u, member.ContributionPoint);
        Assert.Equal(0u, character.Expedition.DailyContributionPoint);
    }

    [Fact]
    public void BuffPurchase_ItemPersistenceFailureRollsBackContributionGradeItemAndHistory()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner,level) VALUES(1,10,8); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo,contribution_point) VALUES(10,1,'Member',55,255,NOW(),1,2,3,'',100); CREATE TRIGGER fail_history_write BEFORE INSERT ON expedition_management_histories FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced history rollback';");
        var itemManager = CreateItemManager();
        var (manager, character, member) = Context(itemManager, currentSession: true, ownerId: 10, level: 8,
            role: byte.MaxValue);
        var item = AttachItem(character, itemManager, 7001, 49000, 2);
        SeedItem(itemManager, item);
        ExpeditionBuffGameData.Instance.SetForTest(new ExpeditionBuffTemplate { Id = 91, Active = true },
            new ExpeditionBuffGrade { Id = 1, ExpeditionBuffId = 91, Grade = 1, Contribution = 40, ItemId = 49000, Count = 1, ExpeditionLevelId = 1 });

        var previousProvider = SingletonContainer.ServiceProvider;
        var services = new ServiceCollection();
        services.AddSingleton(new ExpeditionActivityService(new MySqlExpeditionActivityRepository(fixture),
            new Mock<IWorldManager>().Object, manager, itemManager, fixture, new Mock<IFactionManager>().Object));
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();
        try
        {
            Assert.ThrowsAny<Exception>(() => manager.TryPurchaseBuffGrade(character, 91, 1));
        }
        finally
        {
            (SingletonContainer.ServiceProvider as IDisposable)?.Dispose();
            SingletonContainer.ServiceProvider = previousProvider;
        }

        Assert.Equal(100, Scalar("SELECT contribution_point FROM expedition_members WHERE character_id=10"));
        Assert.Equal(2, Scalar("SELECT count FROM items WHERE id=7001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_buff_purchases"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_management_histories"));
        Assert.Equal(100u, member.ContributionPoint);
        Assert.Equal(2, item.Count);
        Assert.Empty(character.Expedition.PurchasedBuffGrades);
    }

    [Fact]
    public void BuffPurchase_CommitsRealItemSnapshotContributionGradeAndHistoryTogether()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner,level) VALUES(1,10,8); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo,contribution_point) VALUES(10,1,'Member',55,255,NOW(),1,2,3,'',100);");
        var itemManager = CreateItemManager();
        var (manager, character, member) = Context(itemManager, currentSession: true, ownerId: 10, level: 8,
            role: byte.MaxValue);
        var item = AttachItem(character, itemManager, 7002, 49000, 2);
        SeedItem(itemManager, item);
        ExpeditionBuffGameData.Instance.SetForTest(new ExpeditionBuffTemplate { Id = 5, Active = true },
            new ExpeditionBuffGrade { Id = 2, ExpeditionBuffId = 5, Grade = 1, Contribution = 40, ItemId = 49000, Count = 1, ExpeditionLevelId = 1 });
        var previousProvider = SingletonContainer.ServiceProvider;
        var services = new ServiceCollection();
        services.AddSingleton(new ExpeditionActivityService(new MySqlExpeditionActivityRepository(fixture),
            new Mock<IWorldManager>().Object, manager, itemManager, fixture, new Mock<IFactionManager>().Object));
        SingletonContainer.ServiceProvider = services.BuildServiceProvider();
        try
        {
            Assert.True(manager.TryPurchaseBuffGrade(character, 5, 1));
        }
        finally
        {
            (SingletonContainer.ServiceProvider as IDisposable)?.Dispose();
            SingletonContainer.ServiceProvider = previousProvider;
        }

        Assert.Equal(60, Scalar("SELECT contribution_point FROM expedition_members WHERE character_id=10"));
        Assert.Equal(1, Scalar("SELECT count FROM items WHERE id=7002"));
        Assert.Equal(1, Scalar("SELECT grade FROM expedition_buff_purchases WHERE expedition_id=1 AND expedition_buff_id=5"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_management_histories WHERE expedition_id=1 AND history_type=1 AND amount=40 AND detail_id=5 AND detail_value=1"));
        Assert.Equal(60u, member.ContributionPoint);
        Assert.Equal(1, item.Count);
        Assert.Equal((byte)1, character.Expedition.PurchasedBuffGrades[5]);
        Assert.Equal(5, character.Bonuses[Buffs.ExpeditionBonusesIndex].Count);
    }

    [Fact]
    public void ItemLevelUp_CommitsGuildLevelAndRealItemSnapshotTogether()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner,level,exp) VALUES(1,10,1,500); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Member',55,255,NOW(),1,2,3,'');");
        var itemManager = CreateItemManager();
        var (manager, character, _) = Context(itemManager, true, 10, 1, byte.MaxValue);
        character.Expedition.Exp = 500;
        var item = AttachItem(character, itemManager, 7003, 49001, 2);
        SeedItem(itemManager, item);
        ExpeditionLevelGameData.Instance.SetForTest(new ExpeditionLevel { Id = 1 },
            new ExpeditionLevel { Id = 2, TotalExp = 500, RequireItemId = 49001, RequireItemAmount = 1 });

        Assert.True(manager.TryLevelUp(character));

        Assert.Equal(2, Scalar("SELECT level FROM expeditions WHERE id=1"));
        Assert.Equal(1, Scalar("SELECT count FROM items WHERE id=7003"));
        Assert.Equal(2u, character.Expedition.Level);
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public void ItemLevelUp_ItemFailureRollsBackGuildLevelItemAndMemory()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner,level,exp) VALUES(1,10,1,500); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Member',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Member',55,255,NOW(),1,2,3,'');");
        var itemManager = CreateItemManager();
        var (manager, character, _) = Context(itemManager, true, 10, 1, byte.MaxValue);
        character.Expedition.Exp = 500;
        var item = AttachItem(character, itemManager, 7004, 49001, 2);
        SeedItem(itemManager, item);
        Execute("CREATE TRIGGER fail_item_write BEFORE INSERT ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced item rollback';");
        ExpeditionLevelGameData.Instance.SetForTest(new ExpeditionLevel { Id = 1 },
            new ExpeditionLevel { Id = 2, TotalExp = 500, RequireItemId = 49001, RequireItemAmount = 1 });

        Assert.ThrowsAny<Exception>(() => manager.TryLevelUp(character));

        Assert.Equal(1, Scalar("SELECT level FROM expeditions WHERE id=1"));
        Assert.Equal(2, Scalar("SELECT count FROM items WHERE id=7004"));
        Assert.Equal(1u, character.Expedition.Level);
        Assert.Equal(2, item.Count);
    }

    [Fact]
    public void EndWar_CommitsBothGuildOutcomesAndHistoryInOneTransaction()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        SeedWar();
        var (manager, declarer, defender) = WarContext();

        manager.EndWar(declarer.Id);

        Assert.Equal(1, Scalar("SELECT war_wins FROM expeditions WHERE id=1"));
        Assert.Equal(1, Scalar("SELECT war_losses FROM expeditions WHERE id=2"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expeditions WHERE war_ends_at IS NOT NULL"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_war_histories WHERE declarer_id=1 AND defendant_id=2 AND declarer_kills=4 AND defendant_kills=2"));
        Assert.Equal(1u, declarer.WarWins);
        Assert.Equal(1u, defender.WarLosses);
    }

    [Fact]
    public void EndWar_HistoryFailureRollsBackBothGuildsAndRestoresMemory()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        SeedWar();
        Execute("CREATE TRIGGER fail_war_history BEFORE INSERT ON expedition_war_histories FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced war rollback';");
        var (manager, declarer, defender) = WarContext();

        Assert.ThrowsAny<Exception>(() => manager.EndWar(declarer.Id));

        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM expeditions WHERE war_ends_at IS NOT NULL"));
        Assert.Equal(0, Scalar("SELECT war_wins FROM expeditions WHERE id=1"));
        Assert.Equal(0, Scalar("SELECT war_losses FROM expeditions WHERE id=2"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_war_histories"));
        Assert.NotNull(declarer.WarEndsAt);
        Assert.NotNull(defender.WarEndsAt);
        Assert.Equal(0u, declarer.WarWins);
        Assert.Equal(0u, defender.WarLosses);
    }

    [Fact]
    public void DeclareWar_CommitsFeeAndPairedGuildStateTogether()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        SeedWarDeclaration();
        var (manager, owner, target, source, enemy) = WarDeclarationContext();

        manager.DeclareWar(owner.Connection, target.ObjId, 100);

        Assert.Equal(900, Scalar("SELECT money FROM characters WHERE id=10"));
        Assert.Equal(2, Scalar("SELECT war_enemy_expedition_id FROM expeditions WHERE id=1"));
        Assert.Equal(1, Scalar("SELECT war_enemy_expedition_id FROM expeditions WHERE id=2"));
        Assert.Equal(100, Scalar("SELECT war_deposit FROM expeditions WHERE id=1"));
        Assert.Equal(2u, source.WarEnemyExpeditionId);
        Assert.Equal(1u, enemy.WarEnemyExpeditionId);
        Assert.Equal(900L, owner.Money);
    }

    [Fact]
    public void DeclareWar_PairedSaveFailureRollsBackFeeGuildsAndMemory()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        SeedWarDeclaration();
        Execute("CREATE TRIGGER fail_expedition_save BEFORE UPDATE ON expeditions FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced war rollback';");
        var (manager, owner, target, source, enemy) = WarDeclarationContext();

        Assert.ThrowsAny<Exception>(() => manager.DeclareWar(owner.Connection, target.ObjId, 100));

        Assert.Equal(1000, Scalar("SELECT money FROM characters WHERE id=10"));
        Assert.Equal(0, Scalar("SELECT SUM(war_enemy_expedition_id) FROM expeditions"));
        Assert.Equal(0u, source.WarEnemyExpeditionId);
        Assert.Equal(0u, enemy.WarEnemyExpeditionId);
        Assert.Equal(1000L, owner.Money);
    }

    [Fact]
    public void Disband_CommitsRosterAndExplicitGuildOwnedActivityCleanup()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner,name) VALUES(1,10,'Guild'); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Owner',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Owner',55,255,NOW(),1,2,3,''); INSERT INTO expedition_portals(expedition_id,name,zone_id,x,y,z,z_rot) VALUES(1,'Portal',1,1,2,3,0); INSERT INTO expedition_management_histories(expedition_id,member_name,history_type,amount,used_at,detail_id,detail_value) VALUES(1,'Owner',1,1,NOW(),1,1); INSERT INTO expedition_instance_histories(expedition_id,instance_rank_detail_id,instance_id,score,play_result,recorded_at) VALUES(1,1,1,10,1,NOW()); INSERT INTO expedition_instance_history_members(history_id,character_id,status) VALUES(LAST_INSERT_ID(),10,1);");
        var owner = new Character(new())
        {
            Id = 10,
            Name = "Owner",
            Faction = new() { Id = FactionsEnum.NuiaAlliance, MotherId = FactionsEnum.NuiaAlliance }
        };
        owner.Abilities = new CharacterAbilities(owner);
        var guild = new Expedition { Id = (FactionsEnum)1, OwnerId = 10, OwnerName = "Owner", Name = "Guild" };
        var ownerMember = ExpeditionManager.GetMemberFromCharacter(guild, owner, false);
        ownerMember.Role = byte.MaxValue;
        guild.Members.Add(ownerMember);
        owner.Expedition = guild;
        owner.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = owner };
        var world = new Mock<IWorldManager>();
        world.Setup(manager => manager.GetCharacterById(owner.Id)).Returns(owner);
        world.Setup(manager => manager.GetAllCharacters()).Returns([owner]);
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object,
            new Mock<ITeamManager>().Object, world.Object, new Mock<IChatManager>().Object, fixture,
            new Mock<IItemManager>().Object);
        SetField(manager, "_expeditions", new Dictionary<FactionsEnum, Expedition> { [guild.Id] = guild });

        Assert.True(manager.Disband(owner));

        Assert.Equal(0, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_members WHERE expedition_id=1"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_portals WHERE expedition_id=1"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_management_histories WHERE expedition_id=1"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_instance_histories WHERE expedition_id=1"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM expedition_instance_history_members"));
        Assert.Null(owner.Expedition);
        Assert.True(guild.isDisbanded);
    }

    [Fact]
    public void Disband_DependentCleanupFailureRollsBackRosterCharacterAndMemory()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_RECRUITMENT_TEST_MYSQL for an isolated schema.");
        Reset();
        Execute("INSERT INTO expeditions(id,owner,name) VALUES(1,10,'Guild'); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id) VALUES(10,1,'Owner',55,0,1,1,2,3,1); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Owner',55,255,NOW(),1,2,3,''); INSERT INTO expedition_portals(expedition_id,name,zone_id,x,y,z,z_rot) VALUES(1,'Portal',1,1,2,3,0); CREATE TRIGGER fail_dependent_delete BEFORE DELETE ON expedition_portals FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced dependent cleanup rollback';");
        var owner = new Character(new())
        {
            Id = 10,
            Name = "Owner",
            Faction = new() { Id = FactionsEnum.NuiaAlliance, MotherId = FactionsEnum.NuiaAlliance }
        };
        owner.Abilities = new CharacterAbilities(owner);
        var guild = new Expedition { Id = (FactionsEnum)1, OwnerId = 10, OwnerName = "Owner", Name = "Guild" };
        var ownerMember = ExpeditionManager.GetMemberFromCharacter(guild, owner, false);
        ownerMember.Role = byte.MaxValue;
        guild.Members.Add(ownerMember);
        owner.Expedition = guild;
        owner.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = owner };
        var world = new Mock<IWorldManager>();
        world.Setup(manager => manager.GetCharacterById(owner.Id)).Returns(owner);
        world.Setup(manager => manager.GetAllCharacters()).Returns([owner]);
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object,
            new Mock<ITeamManager>().Object, world.Object, new Mock<IChatManager>().Object, fixture,
            new Mock<IItemManager>().Object);
        SetField(manager, "_expeditions", new Dictionary<FactionsEnum, Expedition> { [guild.Id] = guild });

        Assert.ThrowsAny<Exception>(() => manager.Disband(owner));

        Assert.Equal(1, Scalar("SELECT expedition_id FROM characters WHERE id=10"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_members WHERE expedition_id=1"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM expedition_portals WHERE expedition_id=1"));
        Assert.Same(guild, owner.Expedition);
        Assert.Contains(ownerMember, guild.Members);
        Assert.False(guild.isDisbanded);
    }

    private (ExpeditionManager Manager, Character Owner, Character[] Members) CreationContext()
    {
        AppConfiguration.Instance.Expedition ??= new ExpeditionConfig
        {
            Create = new ExpeditionConfigCreate
            {
                Cost = 10_000,
                Level = 5,
                PartyMemberCount = Team.PartyMemberLimit
            },
            NameRegex = "^[a-zA-Z ]{3,32}$",
            RolePolicies = []
        };
        var creation = AppConfiguration.Instance.Expedition.Create;
        Assert.Equal(Team.PartyMemberLimit, (int)creation.PartyMemberCount);
        var sponsor = new AAEmu.Game.Models.Game.Faction.SystemFaction
        {
            Id = FactionsEnum.Nuian,
            MotherId = FactionsEnum.NuiaAlliance,
            ShowCreateExpedition = true
        };
        var members = Enumerable.Range(0, Team.PartyMemberLimit).Select(index =>
        {
            var character = new Character(new())
            {
                Id = (uint)(101 + index),
                ObjId = (uint)(201 + index),
                Name = $"Founder{index + 1}",
                Level = 55,
                Faction = sponsor,
                Money = index == 0 ? creation.Cost + 500 : 0
            };
            character.Abilities = new CharacterAbilities(character);
            character.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = character };
            return character;
        }).ToArray();
        foreach (var member in members)
            Execute($"INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id,money) VALUES({member.Id},{member.Id},'{member.Name}',55,0,{(uint)FactionsEnum.Nuian},1,2,3,0,{member.Money})");

        var team = new Team { IsParty = true, OwnerId = members[0].Id };
        for (var index = 0; index < members.Length; index++)
            team.Members[index] = new TeamMember(members[index]);
        var teams = new Mock<ITeamManager>();
        teams.Setup(manager => manager.GetActiveTeamByUnit(It.IsAny<uint>())).Returns(team);
        var world = new Mock<IWorldManager>();
        world.Setup(manager => manager.GetCharacterById(It.IsAny<uint>()))
            .Returns((uint id) => members.SingleOrDefault(member => member.Id == id));
        world.Setup(manager => manager.GetAllCharacters()).Returns(members.ToList());
        var ids = new Mock<IExpeditionIdManager>();
        ids.Setup(manager => manager.GetNextId()).Returns(50);
        var factions = new Mock<IFactionManager>();
        factions.Setup(manager => manager.GetFaction(FactionsEnum.Nuian)).Returns(sponsor);
        var manager = new ExpeditionManager(ids.Object, teams.Object, world.Object,
            new Mock<IChatManager>().Object, fixture, new Mock<IItemManager>().Object,
            factions.Object, new Mock<ITaskManager>().Object);
        SetField(manager, "_expeditions", new Dictionary<FactionsEnum, Expedition>());
        SetField(manager, "_nameRegex", new System.Text.RegularExpressions.Regex(
            AppConfiguration.Instance.Expedition.NameRegex));
        ExpeditionLevelGameData.Instance.SetForTest(new ExpeditionLevel { Id = 1, MemberLimit = Team.PartyMemberLimit });
        return (manager, members[0], members);
    }

    private (ExpeditionManager Manager, Character Character, ExpeditionMember Member) Context(
        IItemManager itemManager = null, bool currentSession = false, uint ownerId = 99, uint level = 1,
        byte role = 0)
    {
        var character = new Character(new())
        {
            Id = 10,
            Name = "Member",
            Faction = new() { Id = FactionsEnum.NuiaAlliance, MotherId = FactionsEnum.NuiaAlliance }
        };
        character.Abilities = new CharacterAbilities(character);
        var expedition = new Expedition { Id = (FactionsEnum)1, OwnerId = ownerId, OwnerName = string.Empty, Level = level, Name = "Guild" };
        var member = ExpeditionManager.GetMemberFromCharacter(expedition, character, false);
        member.Role = role;
        member.ContributionPoint = 100;
        expedition.Members.Add(member);
        character.Expedition = expedition;
        var world = new Mock<IWorldManager>();
        if (currentSession)
        {
            character.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = character };
            world.Setup(manager => manager.GetCharacterById(character.Id)).Returns(character);
        }
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object,
            new Mock<ITeamManager>().Object, world.Object,
            new Mock<IChatManager>().Object, fixture, itemManager ?? new Mock<IItemManager>().Object);
        var contentConfig = (Dictionary<string, long>)typeof(ExpeditionManager)
            .GetField("_contentConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        contentConfig["expedition_rejoin"] = 24;
        return (manager, character, member);
    }

    private static ItemManager CreateItemManager()
    {
        var manager = new ItemManager(new Mock<ISkillManager>().Object, new Mock<IItemIdManager>().Object,
            new Mock<IContainerIdManager>().Object, new Mock<ILocalizationManager>().Object,
            new Mock<ITaskManager>().Object, new Mock<IWorldManager>().Object);
        SetField(manager, "_allItems", new Dictionary<ulong, Item>());
        SetField(manager, "_removedItems", new List<ulong>());
        SetField(manager, "_allPersistentContainers", new Dictionary<ulong, ItemContainer>());
        SetField(manager, "_itemBagContainers", new Dictionary<ulong, ItemBagContainer>());
        return manager;
    }

    private static Item AttachItem(Character character, ItemManager manager, ulong id, uint templateId, int count)
    {
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var bag = new ItemContainer(character.Id, SlotType.Inventory, false, character)
            { Owner = character, ContainerId = id + 10_000, ContainerSize = 50 };
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var item = new Item(id, new ItemTemplate { Id = templateId, MaxCount = 1000 }, count)
        {
            OwnerId = character.Id, SlotType = SlotType.Inventory, Slot = 1, _holdingContainer = bag,
            CreateTime = now, UnsecureTime = now, UnpackTime = now, ExpirationTime = now.AddYears(1), ChargeStartTime = now
        };
        bag.Items.Add(item);
        bag.UpdateFreeSlotCount();
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(inventory,
            new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
        character.Inventory = inventory;
        Assert.True(manager.AddItem(item));
        return item;
    }

    private void SeedItem(ItemManager manager, Item item)
    {
        using var connection = fixture.Open();
        PersistenceGate.EnterOperation();
        try
        {
            using var transaction = connection.BeginTransaction();
            manager.PersistSnapshots(connection, transaction, [manager.CapturePersistenceSnapshot(item)]);
            transaction.Commit();
        }
        finally { PersistenceGate.ExitOperation(); }
    }

    private void SeedWar() => Execute("INSERT INTO expeditions(id,owner,name,war_enemy_expedition_id,war_declared_at,war_ends_at,war_kill_score,war_is_declarer,war_deposit) VALUES(1,10,'Declarer',2,NOW(),DATE_ADD(NOW(),INTERVAL 1 HOUR),4,1,100),(2,20,'Defender',1,NOW(),DATE_ADD(NOW(),INTERVAL 1 HOUR),2,0,0);");

    private void SeedWarDeclaration() => Execute("INSERT INTO expeditions(id,owner,owner_name,name) VALUES(1,10,'Owner','Source'),(2,20,'Target','Enemy'); INSERT INTO characters(id,account_id,name,level,heir_exp,faction_id,ability1,ability2,ability3,expedition_id,money) VALUES(10,1,'Owner',55,0,148,1,2,3,1,1000),(20,2,'Target',55,0,148,1,2,3,2,0); INSERT INTO expedition_members(character_id,expedition_id,name,level,role,last_leave_time,ability1,ability2,ability3,memo) VALUES(10,1,'Owner',55,255,NOW(),1,2,3,''),(20,2,'Target',55,0,NOW(),1,2,3,'');");

    private (ExpeditionManager Manager, Character Owner, Character Target, Expedition Source, Expedition Enemy) WarDeclarationContext()
    {
        var faction = new AAEmu.Game.Models.Game.Faction.SystemFaction { Id = FactionsEnum.NuiaAlliance, MotherId = FactionsEnum.NuiaAlliance };
        var owner = new Character(new()) { Id = 10, ObjId = 110, Name = "Owner", Faction = faction, Money = 1000 };
        var target = new Character(new()) { Id = 20, ObjId = 120, Name = "Target", Faction = faction };
        owner.Abilities = new CharacterAbilities(owner);
        target.Abilities = new CharacterAbilities(target);
        owner.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = owner };
        target.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = target };
        var source = new Expedition { Id = (FactionsEnum)1, OwnerId = 10, OwnerName = "Owner", Name = "Source", Members = [] };
        var enemy = new Expedition { Id = (FactionsEnum)2, OwnerId = 20, OwnerName = "Target", Name = "Enemy", Members = [] };
        var ownerMember = ExpeditionManager.GetMemberFromCharacter(source, owner, true);
        var targetMember = ExpeditionManager.GetMemberFromCharacter(enemy, target, false);
        source.Members.Add(ownerMember);
        enemy.Members.Add(targetMember);
        owner.Expedition = source;
        target.Expedition = enemy;
        var world = new Mock<IWorldManager>();
        world.Setup(manager => manager.GetCharacterById(owner.Id)).Returns(owner);
        world.Setup(manager => manager.GetCharacterById(target.Id)).Returns(target);
        world.Setup(manager => manager.GetCharacterByObjId(target.ObjId)).Returns(target);
        world.Setup(manager => manager.GetAllCharacters()).Returns([owner, target]);
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object, new Mock<ITeamManager>().Object,
            world.Object, new Mock<IChatManager>().Object, fixture, new Mock<IItemManager>().Object,
            new Mock<IFactionManager>().Object, new Mock<ITaskManager>().Object);
        var config = (Dictionary<string, long>)typeof(ExpeditionManager)
            .GetField("_contentConfig", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        config["expedition_war_initial_money_for_declaration"] = 100;
        config["expedition_war_duration"] = 3_600_000;
        return (manager, owner, target, source, enemy);
    }

    private (ExpeditionManager Manager, Expedition Declarer, Expedition Defender) WarContext()
    {
        var world = new Mock<IWorldManager>();
        world.Setup(manager => manager.GetAllCharacters()).Returns([]);
        var manager = new ExpeditionManager(new Mock<IExpeditionIdManager>().Object,
            new Mock<ITeamManager>().Object, world.Object, new Mock<IChatManager>().Object, fixture,
            new Mock<IItemManager>().Object);
        var declaredAt = DateTime.UtcNow.AddMinutes(-30);
        var endsAt = DateTime.UtcNow.AddMinutes(30);
        var declarer = new Expedition { Id = (FactionsEnum)1, OwnerId = 10, OwnerName = "Declarer Owner", Name = "Declarer", WarEnemyExpeditionId = 2, WarDeclaredAt = declaredAt, WarEndsAt = endsAt, WarKillScore = 4, WarIsDeclarer = true, WarDeposit = 100 };
        var defender = new Expedition { Id = (FactionsEnum)2, OwnerId = 20, OwnerName = "Defender Owner", Name = "Defender", WarEnemyExpeditionId = 1, WarDeclaredAt = declaredAt, WarEndsAt = endsAt, WarKillScore = 2 };
        SetField(manager, "_expeditions", new Dictionary<FactionsEnum, Expedition> { [declarer.Id] = declarer, [defender.Id] = defender });
        return (manager, declarer, defender);
    }

    private void Reset() => Execute("DROP TRIGGER IF EXISTS fail_expedition_save; DROP TRIGGER IF EXISTS fail_history_write; DROP TRIGGER IF EXISTS fail_war_history; DROP TRIGGER IF EXISTS fail_item_write; DROP TRIGGER IF EXISTS fail_founder_roster; DROP TRIGGER IF EXISTS fail_dependent_delete; SET FOREIGN_KEY_CHECKS=0; TRUNCATE expedition_instance_history_members; TRUNCATE expedition_instance_histories; TRUNCATE expedition_portals; TRUNCATE expedition_members; TRUNCATE expedition_buff_purchases; TRUNCATE expedition_management_histories; TRUNCATE expedition_war_histories; TRUNCATE expedition_daily_activity; TRUNCATE items; TRUNCATE item_containers; TRUNCATE expeditions; TRUNCATE characters; SET FOREIGN_KEY_CHECKS=1;");
    private void Execute(string sql) { using var c = fixture.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private long Scalar(string sql) { using var c = fixture.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar()); }
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExpeditionCoreStaticCollection
{
    public const string Name = "Expedition core static state";
}
