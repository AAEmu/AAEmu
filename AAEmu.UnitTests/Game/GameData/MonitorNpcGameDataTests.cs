using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using Microsoft.Data.Sqlite;
using TUnit.Assertions.Enums;

namespace AAEmu.UnitTests.Game.GameData;

public class MonitorNpcGameDataTests
{
    [Test]
    public async Task SnapshotTracksActualLiveInstancesAndOnlyClearsAfterTheLastDespawn()
    {
        var monitor = Load(16173, 16174);

        monitor.OnSpawn(100, 16173);
        monitor.OnSpawn(101, 16173);
        monitor.OnSpawn(102, 99999);
        await Assert.That(monitor.GetSpawnedTemplates()).IsEquivalentTo(new uint[] { 16173 });

        monitor.OnRemove(100);
        await Assert.That(monitor.GetSpawnedTemplates()).IsEquivalentTo(new uint[] { 16173 });

        monitor.OnRemove(101);
        await Assert.That(monitor.GetSpawnedTemplates()).IsEmpty();
    }

    [Test]
    public async Task DuplicateLifecycleCallbacksAreIdempotentForLateJoinSnapshot()
    {
        var monitor = Load(8553);

        monitor.OnSpawn(200, 8553);
        monitor.OnSpawn(200, 8553);
        monitor.OnRemove(200);
        monitor.OnRemove(200);

        await Assert.That(monitor.GetSpawnedTemplates()).IsEmpty();
    }

    [Test]
    public async Task MonitorPacketsMatchTheNativeCountListAndEdgeBodies()
    {
        var list = new SCSpawnedMonitorNpcsPacket([16173, 8553]).Write(new PacketStream());
        list.Rollback();
        await Assert.That(list.ReadUInt16()).IsEqualTo((ushort)2);
        await Assert.That(list.ReadUInt32()).IsEqualTo(16173u);
        await Assert.That(list.ReadUInt32()).IsEqualTo(8553u);
        await Assert.That(list.LeftBytes).IsEqualTo(0);

        var edge = new SCMonitorNpcSpawnedPacket(16173, true).Write(new PacketStream());
        edge.Rollback();
        await Assert.That(edge.ReadInt32()).IsEqualTo(16173);
        await Assert.That(edge.ReadBoolean()).IsTrue();
        await Assert.That(edge.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentSpawnAndRemovePublishEdgesInStateOrder()
    {
        var edges = new List<(uint TemplateId, bool Spawned)>();
        var enteredSpawnPublish = new ManualResetEventSlim();
        var releaseSpawnPublish = new ManualResetEventSlim();
        var monitor = Load((id, spawned) =>
        {
            if (spawned)
            {
                enteredSpawnPublish.Set();
                releaseSpawnPublish.Wait();
            }
            edges.Add((id, spawned));
        }, 16173);

        var spawn = Task.Run(() => monitor.OnSpawn(300, 16173));
        enteredSpawnPublish.Wait();
        var remove = Task.Run(() => monitor.OnRemove(300));
        await Task.Delay(25);
        await Assert.That(edges).IsEmpty();

        releaseSpawnPublish.Set();
        await Task.WhenAll(spawn, remove);
        await Assert.That(edges).IsEquivalentTo(new[] { (16173u, true), (16173u, false) },
            CollectionOrdering.Matching);
        await Assert.That(monitor.GetSpawnedTemplates()).IsEmpty();
    }

    [Test]
    public async Task BulkDisconnectRemovalKeepsTemplateUntilEveryOwnedInstanceIsGone()
    {
        var monitor = Load(16174);
        monitor.OnSpawn(400, 16174);
        monitor.OnSpawn(401, 16174);

        foreach (var objectId in new uint[] { 400, 401 })
            monitor.OnRemove(objectId);

        await Assert.That(monitor.GetSpawnedTemplates()).IsEmpty();
    }

    [Test]
    public async Task SnapshotPublicationCannotOvertakeAnEarlierSpawnEdge()
    {
        var order = new List<string>();
        var enteredEdge = new ManualResetEventSlim();
        var releaseEdge = new ManualResetEventSlim();
        var monitor = Load((_, spawned) =>
        {
            if (!spawned)
                return;
            enteredEdge.Set();
            releaseEdge.Wait();
            order.Add("edge");
        }, 8553);

        var spawn = Task.Run(() => monitor.OnSpawn(500, 8553));
        enteredEdge.Wait();
        var snapshot = Task.Run(() => monitor.PublishSnapshot(ids =>
            order.Add(ids.SequenceEqual(new uint[] { 8553 }) ? "snapshot" : "stale")));
        await Task.Delay(25);
        await Assert.That(order).IsEmpty();

        releaseEdge.Set();
        await Task.WhenAll(spawn, snapshot);
        await Assert.That(order).IsEquivalentTo(new[] { "edge", "snapshot" }, CollectionOrdering.Matching);
    }

    private static MonitorNpcGameData Load(params uint[] npcIds) => Load((_, _) => { }, npcIds);

    private static MonitorNpcGameData Load(Action<uint, bool> publish, params uint[] npcIds)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE monitor_npcs (id INTEGER PRIMARY KEY, npc_id INTEGER NOT NULL)";
            command.ExecuteNonQuery();
        }

        for (var i = 0; i < npcIds.Length; i++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO monitor_npcs (id, npc_id) VALUES ($id, $npc)";
            command.Parameters.AddWithValue("$id", i + 1);
            command.Parameters.AddWithValue("$npc", npcIds[i]);
            command.ExecuteNonQuery();
        }

        var result = new MonitorNpcGameData(publish);
        result.Load(connection);
        return result;
    }
}
