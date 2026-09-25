using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Mates;

[NotInParallel]
public class CharacterMatesPersistenceTests
{
    [Test]
    public async Task SaveLoad_RoundTripsRecoverySnapshots()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        InsertMate(connection, 102, 1002, null, null, null);
        var (owner, mates) = CreateOwnerAndMates(77);

        mates.Load(connection);
        await Assert.That(mates.GetMateInfo(1001).Xp).IsEqualTo(10);
        await Assert.That(mates.GetMateInfo(1001).RecoveryState.HasValue).IsFalse();
        await Assert.That(mates.GetMateInfo(1002).RecoveryState.HasValue).IsFalse();

        mates.UpdateMateInfo(1001, db => db.Xp = 17);
        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            mates.PrepareSaveCommit(connection, transaction);
            transaction.Commit();
            mates.ConfirmSave(transaction);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001).Xp).IsEqualTo(17);
        await Assert.That(reloaded.GetMateInfo(1001).RecoveryState.HasValue).IsFalse();
        await Assert.That(owner.Id).IsEqualTo(77u);
    }

    [Test]
    public async Task Removal_RollbackKeepsRow_CommitRemovesIt()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);

        await Assert.That(mates.RemoveMate(1001)).IsTrue();
        using (var rolledBack = connection.BeginTransaction())
        {
            mates.Save(connection, rolledBack);
            rolledBack.Rollback();
        }

        var (_, afterRollback) = CreateOwnerAndMates(77);
        afterRollback.Load(connection);
        await Assert.That(afterRollback.GetMateInfo(1001)).IsNotNull();
        await Assert.That(afterRollback.GetMateInfo(1001).Xp).IsEqualTo(10);

        using (var committed = connection.BeginTransaction())
        {
            mates.Save(connection, committed);
            mates.PrepareSaveCommit(connection, committed);
            committed.Commit();
            mates.ConfirmSave(committed);
        }

        var (_, afterCommit) = CreateOwnerAndMates(77);
        afterCommit.Load(connection);
        await Assert.That(afterCommit.GetMateInfo(1001)).IsNull();
    }

    [Test]
    public async Task Removal_UsesCompositeKey_AndKeepsSameNpcOtherItem()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        InsertMate(connection, 101, 1002, 17, 19, 23);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);

        await Assert.That(mates.RemoveMate(1001)).IsTrue();
        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            mates.PrepareSaveCommit(connection, transaction);
            transaction.Commit();
            mates.ConfirmSave(transaction);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001)).IsNull();
        await Assert.That(reloaded.GetMateInfo(1002)).IsNotNull();
        await Assert.That(reloaded.GetMateInfo(1002).Xp).IsEqualTo(10);
    }

    [Test]
    public async Task Removal_ReAddDuringOpenSave_IsFlushedBeforeCommit()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);
        var removed = mates.GetMateInfo(1001);

        await Assert.That(mates.RemoveMate(1001)).IsTrue();
        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            mates.RestoreMate(removed);
            mates.PrepareSaveCommit(connection, transaction);
            transaction.Commit();
            mates.ConfirmSave(transaction);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001)).IsNotNull();
        await Assert.That(reloaded.GetMateInfo(1001).Xp).IsEqualTo(10);
    }

    [Test]
    public async Task Removal_NewerMarkerSurvivesOlderCommit()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        InsertMate(connection, 102, 1002, 17, 19, 23);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);

        await Assert.That(mates.RemoveMate(1001)).IsTrue();
        using (var firstSave = connection.BeginTransaction())
        {
            mates.Save(connection, firstSave);
            await Assert.That(mates.RemoveMate(1002)).IsTrue();
            mates.PrepareSaveCommit(connection, firstSave);
            firstSave.Commit();
            mates.ConfirmSave(firstSave);
        }

        using (var secondSave = connection.BeginTransaction())
        {
            mates.Save(connection, secondSave);
            mates.PrepareSaveCommit(connection, secondSave);
            secondSave.Commit();
            mates.ConfirmSave(secondSave);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001)).IsNull();
        await Assert.That(reloaded.GetMateInfo(1002)).IsNull();
    }

    [Test]
    public async Task CaptureActiveMateState_PersistsDespawnRecovery()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);
        var active = new Mate
        {
            ItemId = 1001,
            Name = "synthetic-active-mate",
            Level = 9,
            Experience = 123,
            Mileage = 45,
            Hp = 67,
            Mp = 89,
            RecoveryState = new MateRecoveryState(29, 31, 37)
        };

        mates.CaptureActiveMateState(active);
        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            mates.PrepareSaveCommit(connection, transaction);
            transaction.Commit();
            mates.ConfirmSave(transaction);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        var saved = reloaded.GetMateInfo(1001);
        await Assert.That(saved.Hp).IsEqualTo(67);
        await Assert.That(saved.Mp).IsEqualTo(89);
        await Assert.That(saved.Xp).IsEqualTo(123);
        await Assert.That(saved.Level).IsEqualTo((ushort)9);
        await Assert.That(saved.RecoveryState.HasValue).IsFalse();
    }

    [Test]
    public async Task MateInfoSnapshotsAreDetached_AndUpdatesUseGate()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);

        var snapshot = mates.GetMateInfo(1001);
        snapshot.Hp = 1;
        await Assert.That(mates.GetMateInfo(1001).Hp).IsEqualTo(70);

        var updated = mates.UpdateMateInfo(1001, db => db.Hp = 2);
        await Assert.That(updated.Hp).IsEqualTo(2);
        updated.Hp = 3;
        await Assert.That(mates.GetMateInfo(1001).Hp).IsEqualTo(2);
    }

    [Test]
    public async Task CharacterSaveTransactionSeam_RollbackDiscardsAndPreservesMarker()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (owner, mates) = CreateOwnerAndMates(77);
        owner.Mates = mates;
        mates.Load(connection);
        await Assert.That(mates.RemoveMate(1001)).IsTrue();

        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            owner.RollbackMatesSaveTransaction(transaction);
        }

        await Assert.That(mates.RemoveMate(1001)).IsFalse();
        using (var retry = connection.BeginTransaction())
        {
            mates.Save(connection, retry);
            owner.CommitMatesSaveTransaction(connection, retry);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001)).IsNull();
    }

    [Test]
    public async Task HeroManagerTransactionSeam_RollbackDiscardsAndPreservesMarker()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (owner, mates) = CreateOwnerAndMates(77);
        owner.Mates = mates;
        mates.Load(connection);
        await Assert.That(mates.RemoveMate(1001)).IsTrue();

        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            HeroManager.RollbackMatesSaveTransaction(transaction, [owner]);
        }

        using (var retry = connection.BeginTransaction())
        {
            mates.Save(connection, retry);
            HeroManager.CommitMatesSaveTransaction(connection, retry, [owner]);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001)).IsNull();
    }

    [Test]
    public async Task DirectSaveFailure_DiscardsGate_AndNextSaveRemovesMarker()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);
        await AssertRollbackDiscardsGateAndNextSaveDeletes(connection, mates);
    }

    [Test]
    public async Task HeroSaveFailure_DiscardsGate_AndNextSaveRemovesMarker()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);
        await AssertRollbackDiscardsGateAndNextSaveDeletes(connection, mates);
    }

    [Test]
    public async Task CommitConfirm_LeavesNoMarker_AndNextSaveRemovesRow()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);
        await Assert.That(mates.RemoveMate(1001)).IsTrue();

        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            mates.PrepareSaveCommit(connection, transaction);
            transaction.Commit();
            mates.ConfirmSave(transaction);
        }

        using (var nextSave = connection.BeginTransaction())
        {
            mates.Save(connection, nextSave);
            mates.PrepareSaveCommit(connection, nextSave);
            nextSave.Commit();
            mates.ConfirmSave(nextSave);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001)).IsNull();
    }

    private static async Task AssertRollbackDiscardsGateAndNextSaveDeletes(
        SqliteConnection connection,
        CharacterMates mates)
    {
        var original = mates.GetMateInfo(1001);
        await Assert.That(mates.RemoveMate(1001)).IsTrue();
        using (var transaction = connection.BeginTransaction())
        {
            mates.Save(connection, transaction);
            mates.PrepareSaveCommit(connection, transaction);
            Assert.Throws<InvalidOperationException>(() =>
                mates.UpdateMateInfo(1001, db => db.Hp = 1));
            transaction.Rollback();
            mates.DiscardSave(transaction);
        }

        await Assert.That(original).IsNotNull();
        await Assert.That(mates.RemoveMate(1001)).IsFalse();
        using (var nextSave = connection.BeginTransaction())
        {
            mates.Save(connection, nextSave);
            mates.PrepareSaveCommit(connection, nextSave);
            nextSave.Commit();
            mates.ConfirmSave(nextSave);
        }

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001)).IsNull();
    }

    [Test]
    public async Task DespawnMate_UsesRealMateManagerPath_AndCapturesRecovery()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001, 7, 11, 13);
        using var worldScope = TestDungeonWorld.InstallWorldManager();
        using var world = TestDungeonWorld.CreateWorld(9910, 0);
        using var taskScope = new SingletonScope<TaskManager>(new TaskManager(Mock.Of<ITickManager>().Object));
        world.MateManager = new MateManager(world);
        var owner = new Character(new UnitCustomModelParams())
        {
            Id = 77,
            ObjId = 7001
        };
        TestDungeonWorld.Enter(world, owner);
        owner.Mates = new CharacterMates(owner);
        owner.Mates.Load(connection);

        var active = new Mate
        {
            ObjId = 8001,
            TlId = 9001,
            OwnerId = owner.Id,
            OwnerObjId = owner.ObjId,
            ItemId = 1001,
            Name = "synthetic-active-mate",
            Hp = 67,
            Mp = 89,
            Level = 9,
            Experience = 123,
            Mileage = 45,
            RecoveryState = new MateRecoveryState(29, 31, 37)
        };
        active.ParentWorld = world;
        var activeMates = (Dictionary<uint, List<Mate>>)typeof(MateManager)
            .GetField("_activeMates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world.MateManager)!;
        activeMates[owner.Id] = [active];

        owner.Mates.DespawnMate(active.TlId);

        await Assert.That(world.MateManager.GetActiveMateByTlId(active.TlId)).IsNull();
        var saved = owner.Mates.GetMateInfo(1001);
        await Assert.That(saved.Hp).IsEqualTo(67);
        await Assert.That(saved.Mp).IsEqualTo(89);
        await Assert.That(saved.RecoveryState!.Value).IsEqualTo(active.RecoveryState);
    }

    private static (Character Owner, CharacterMates Mates) CreateOwnerAndMates(uint ownerId)
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = ownerId };
        return (owner, new CharacterMates(owner));
    }

    private static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE mates (
                id INTEGER NOT NULL,
                item_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                xp INTEGER NOT NULL,
                level INTEGER NOT NULL,
                mileage INTEGER NOT NULL,
                hp INTEGER NOT NULL,
                mp INTEGER NOT NULL,
                owner INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                PRIMARY KEY (id, item_id, owner));
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static void InsertMate(
        SqliteConnection connection,
        uint id,
        ulong itemId,
        int? reviveDelay,
        int? reviveHpPercent,
        int? reviveMpPercent)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mates(
                id, item_id, name, xp, level, mileage, hp, mp, owner,
                updated_at, created_at)
            VALUES ($id, $item, 'synthetic-mate', 10, 5, 6, 70, 80, 77,
                $updated, $created);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$updated", DateTime.UtcNow);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow);
        command.ExecuteNonQuery();
    }
}
