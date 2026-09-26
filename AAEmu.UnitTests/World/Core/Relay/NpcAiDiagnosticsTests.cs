using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.World.Core.Relay;

public class NpcAiDiagnosticsTests
{
    [Test]
    public async Task TrackedSpawnBody_ReportsSpawnerAndTemplateWithoutInventingAnOwner()
    {
        var snapshot = NpcAiDiagnostics.SnapshotFrom(null, true, CreateSpawnBody());

        await Assert.That(snapshot.Tracked).IsTrue();
        await Assert.That(snapshot.IsNpc).IsTrue();
        await Assert.That(snapshot.TemplateId).IsEqualTo(3463u);
        await Assert.That(snapshot.SpawnerId).IsEqualTo(7001u);
        await Assert.That(snapshot.SpawnerType).IsEqualTo(3360u);
        await Assert.That(snapshot.TransformZoneId).IsEqualTo(0u);
    }

    [Test]
    public async Task KnownPlayerBody_IsNotMisclassifiedAsAnNpcSpawn()
    {
        var misleadingBody = CreateSpawnBody();
        var snapshot = NpcAiDiagnostics.SnapshotFrom(new Character(new UnitCustomModelParams()), true, misleadingBody);

        await Assert.That(snapshot.Tracked).IsTrue();
        await Assert.That(snapshot.IsPlayer).IsTrue();
        await Assert.That(snapshot.IsNpc).IsFalse();
        await Assert.That(snapshot.TemplateId).IsEqualTo(0u);
        await Assert.That(snapshot.SpawnerId).IsEqualTo(0u);
    }

    private static byte[] CreateSpawnBody() =>
        new PacketStream()
            .Write(7001u)
            .Write(3360u)
            .Write((byte)0)
            .Write((byte)0)
            .Write((ushort)0)
            .Write(3463u)
            .Write(0u)
            .Write(0u)
            .Write((byte)0)
            .Write(1f).Write(2f).Write(3f).Write(0f).Write(1f)
            .Write(new byte[37])
            .GetBytes();
}
