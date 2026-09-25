using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Mates;

/// <summary>
/// Mate progress write-through. A mate row is only ever upserted, so <c>Save</c> takes a snapshot
/// under the save lock and no mate write is ever rejected because a save transaction is in flight:
/// the party-kill and logout paths below run exactly like the shipped gameplay threads do.
/// </summary>
[NotInParallel]
public class CharacterMatesPersistenceTests
{
    [Test]
    public async Task SaveLoad_RoundTripsProgress()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001);
        InsertMate(connection, 102, 1002);
        var (owner, mates) = CreateOwnerAndMates(77);

        mates.Load(connection);
        await Assert.That(mates.GetMateInfo(1001).Xp).IsEqualTo(10);
        await Assert.That(mates.GetMateInfo(1002).Level).IsEqualTo((ushort)5);

        mates.UpdateMateInfo(1001, db => db.Xp = 17);
        SaveAndCommit(connection, mates);

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001).Xp).IsEqualTo(17);
        // A mate the save did not touch keeps its own row.
        await Assert.That(reloaded.GetMateInfo(1002)).IsNotNull();
        await Assert.That(reloaded.GetMateInfo(1002).Xp).IsEqualTo(10);
        await Assert.That(owner.Id).IsEqualTo(77u);
    }

    [Test]
    public async Task MateInfoSnapshotsAreDetached_AndUpdatesWriteThrough()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);

        // A detached snapshot can never mutate the owned row behind the update API.
        var snapshot = mates.GetMateInfo(1001);
        snapshot.Hp = 1;
        await Assert.That(mates.GetMateInfo(1001).Hp).IsEqualTo(70);

        var updated = mates.UpdateMateInfo(1001, db => db.Hp = 2);
        await Assert.That(updated.Hp).IsEqualTo(2);
        updated.Hp = 3;
        await Assert.That(mates.GetMateInfo(1001).Hp).IsEqualTo(2);

        // The identity columns are preserved by an update that does not touch them.
        var afterIdentity = mates.UpdateMateInfo(1001, db => db.Xp = 42);
        await Assert.That(afterIdentity.Id).IsEqualTo(101u);
        await Assert.That(afterIdentity.ItemId).IsEqualTo(1001ul);
        await Assert.That(afterIdentity.Owner).IsEqualTo(77u);
    }

    [Test]
    public async Task UpdateMateInfo_ForUnknownItem_IsANoOp()
    {
        var (_, mates) = CreateOwnerAndMates(77);

        await Assert.That(mates.UpdateMateInfo(9999, db => db.Xp = 5)).IsNull();
    }

    [Test]
    public async Task CaptureActiveMateState_PersistsSummonedProgress()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001);
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
            Mp = 89
        };

        mates.CaptureActiveMateState(active);
        SaveAndCommit(connection, mates);

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        var saved = reloaded.GetMateInfo(1001);
        await Assert.That(saved.Hp).IsEqualTo(67);
        await Assert.That(saved.Mp).IsEqualTo(89);
        await Assert.That(saved.Xp).IsEqualTo(123);
        await Assert.That(saved.Level).IsEqualTo((ushort)9);
        await Assert.That(saved.Mileage).IsEqualTo(45);
    }

    /// <summary>
    /// The party-kill loop in <c>Npc.DoDie</c> calls <c>Mate.AddExp</c> per mate, and that is the
    /// only write <c>AddExp</c> makes to the owned row. Before the gate was removed a save in its
    /// commit window made this call throw, which aborted the loop and cost the rest of the party
    /// their mate XP, honor and quest credit.
    /// </summary>
    [Test]
    public async Task PartyKillLoop_DuringWorldSave_DoesNotThrow_AndReachesEveryMate()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001);
        InsertMate(connection, 102, 1002);
        InsertMate(connection, 103, 1003);
        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);

        // A world save has already snapshotted this character and has not committed yet.
        using var worldSave = connection.BeginTransaction();
        mates.Save(connection, worldSave);

        var awarded = new List<ulong>();
        foreach (var itemId in new ulong[] { 1001, 1002, 1003 })
        {
            // Mate.AddExp -> owner.Mates.UpdateMateInfo(itemId, db => { db.Xp; db.Level; }).
            var updated = mates.UpdateMateInfo(itemId, db => db.Xp = db.Xp + 5);
            awarded.Add(itemId);
            await Assert.That(updated).IsNotNull();
        }
        await Assert.That(awarded).IsEquivalentTo(new ulong[] { 1001, 1002, 1003 });

        worldSave.Commit();

        // The next save carries the XP the kill loop awarded during the previous one.
        SaveAndCommit(connection, mates);
        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        await Assert.That(reloaded.GetMateInfo(1001).Xp).IsEqualTo(15);
        await Assert.That(reloaded.GetMateInfo(1002).Xp).IsEqualTo(15);
        await Assert.That(reloaded.GetMateInfo(1003).Xp).IsEqualTo(15);
    }

    /// <summary>
    /// <c>GameConnection.OnDisconnect</c> reaches <c>RemoveAndDespawnAllActiveOwnedMates</c> before
    /// <c>SaveAndRemoveFromWorld</c>. A throw inside that capture stranded the character in the world
    /// with no save at all, so the logout capture must tolerate an in-flight world save.
    /// </summary>
    [Test]
    public async Task LogoutDuringWorldSave_CapturesProgressAndDoesNotThrow()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001);
        using var worldScope = TestDungeonWorld.InstallWorldManager();
        using var world = TestDungeonWorld.CreateWorld(9910, 0);
        using var taskScope = new SingletonScope<TaskManager>(new TaskManager(Mock.Of<ITickManager>().Object));
        world.MateManager = new MateManager(world);
        world.SlaveManager = new SlaveManager(world);
        var owner = new Character(new UnitCustomModelParams())
        {
            Id = 77,
            ObjId = 7001
        };
        TestDungeonWorld.Enter(world, owner);
        owner.Mates = new CharacterMates(owner);
        owner.Mates.Load(connection);
        AddActiveMate(world, owner, new Mate
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
            Mileage = 45
        });

        // A world save is mid-commit when the socket drops.
        using var worldSave = connection.BeginTransaction();
        owner.Mates.Save(connection, worldSave);

        // The first statement of the production logout chain, and the one that holds the mate
        // capture. (Its wrapper's slave half needs a live Character.Connection, which this PR
        // does not touch.)
        world.MateManager.RemoveAndDespawnAllActiveOwnedMates(owner);

        // The capture must have run even though the world save never committed.
        var captured = owner.Mates.GetMateInfo(1001);
        await Assert.That(captured.Xp).IsEqualTo(123);
        await Assert.That(captured.Hp).IsEqualTo(67);

        worldSave.Commit();

        // The logout's own save then persists the captured progress.
        SaveAndCommit(connection, owner.Mates);
        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        var saved = reloaded.GetMateInfo(1001);
        await Assert.That(saved.Xp).IsEqualTo(123);
        await Assert.That(saved.Level).IsEqualTo((ushort)9);
        await Assert.That(saved.Hp).IsEqualTo(67);
        await Assert.That(saved.Mp).IsEqualTo(89);
    }

    [Test]
    public async Task DespawnMate_UsesRealMateManagerPath_AndCapturesProgress()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001);
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
            Mileage = 45
        };
        AddActiveMate(world, owner, active);

        owner.Mates.DespawnMate(active.TlId);

        await Assert.That(world.MateManager.GetActiveMateByTlId(active.TlId)).IsNull();
        var saved = owner.Mates.GetMateInfo(1001);
        await Assert.That(saved.Hp).IsEqualTo(67);
        await Assert.That(saved.Mp).IsEqualTo(89);
        await Assert.That(saved.Xp).IsEqualTo(123);
    }

    /// <summary>
    /// The live mate carries the authored recovery values; nothing recovery-related is persisted,
    /// so the owned row schema must not grow a column for it and the values must come back from
    /// content on the next summon instead of from a stored profile.
    /// </summary>
    [Test]
    public async Task RecoveryValues_LiveOnTheMate_AndAreNotPersisted()
    {
        using var connection = CreateConnection();
        InsertMate(connection, 101, 1001);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('mates') WHERE name LIKE 'mate_revive%'";
            await Assert.That(Convert.ToInt32(command.ExecuteScalar())).IsEqualTo(0);
        }

        var (_, mates) = CreateOwnerAndMates(77);
        mates.Load(connection);
        var recovery = new MateRecoveryState(29, 31, 37);
        var active = new Mate
        {
            ItemId = 1001,
            Name = "synthetic-active-mate",
            Hp = 70,
            Mp = 80,
            RecoveryState = recovery
        };

        mates.CaptureActiveMateState(active);
        SaveAndCommit(connection, mates);

        var (_, reloaded) = CreateOwnerAndMates(77);
        reloaded.Load(connection);
        // Progress still round-trips; the recovery profile is not part of the row.
        await Assert.That(reloaded.GetMateInfo(1001).Hp).IsEqualTo(70);
        await Assert.That(active.RecoveryState).IsEqualTo(recovery);
    }

    private static void AddActiveMate(WorldInstance world, Character owner, Mate mate)
    {
        mate.ParentWorld = world;
        var activeMates = (Dictionary<uint, List<Mate>>)typeof(MateManager)
            .GetField("_activeMates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world.MateManager)!;
        activeMates[owner.Id] = [mate];
    }

    private static void SaveAndCommit(SqliteConnection connection, CharacterMates mates)
    {
        using var transaction = connection.BeginTransaction();
        mates.Save(connection, transaction);
        transaction.Commit();
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

    private static void InsertMate(SqliteConnection connection, uint id, ulong itemId)
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
