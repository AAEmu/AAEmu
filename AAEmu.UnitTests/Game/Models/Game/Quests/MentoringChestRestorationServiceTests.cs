using System.Reflection;

using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class MentoringChestRestorationServiceTests
{
    private static readonly MentoringChestTrigger Trigger = new(50, 11364, 7522, 20831, 21043);

    [Test]
    public async Task Prepare_RequiresTheMatchingDungeonAndBoss()
    {
        var npc = new Npc { TemplateId = 11364 };
        var spawns = 0;

        var outsideDungeon = Prepare(npc, false, 50, false, () => spawns++);
        var wrongZone = Prepare(npc, true, 47, false, () => spawns++);
        var wrongBoss = Prepare(new Npc { TemplateId = 1 }, true, 50, false, () => spawns++);

        await Assert.That(outsideDungeon).IsEqualTo(MentoringChestPreparationResult.NotApplicable);
        await Assert.That(wrongZone).IsEqualTo(MentoringChestPreparationResult.NotApplicable);
        await Assert.That(wrongBoss).IsEqualTo(MentoringChestPreparationResult.NotApplicable);
        await Assert.That(spawns).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_ReusesAnExistingChestWithoutChangingIt()
    {
        var npc = new Npc { TemplateId = 11364 };
        var spawns = 0;

        var result = Prepare(npc, true, 50, true, () => spawns++);

        await Assert.That(result).IsEqualTo(MentoringChestPreparationResult.ReusedExisting);
        await Assert.That(spawns).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_CreatesOnlyOncePerNpcLife()
    {
        var npc = new Npc { TemplateId = 11364 };
        var spawns = 0;

        var first = Prepare(npc, true, 50, false, () => spawns++);
        var duplicate = Prepare(npc, true, 50, false, () => spawns++);
        npc.ResetMentoringChestPreparation();
        var afterRespawn = Prepare(npc, true, 50, false, () => spawns++);

        await Assert.That(first).IsEqualTo(MentoringChestPreparationResult.Spawned);
        await Assert.That(duplicate).IsEqualTo(MentoringChestPreparationResult.AlreadyPrepared);
        await Assert.That(afterRespawn).IsEqualTo(MentoringChestPreparationResult.Spawned);
        await Assert.That(spawns).IsEqualTo(2);
    }

    [Test]
    public async Task PrepareLocked_SerializesBossVariantsInOneWorld()
    {
        var sync = new object();
        var chestExists = false;
        var spawns = 0;
        var first = new Npc { TemplateId = 11364 };
        var second = new Npc { TemplateId = 11364 };

        var results = await Task.WhenAll(
            Task.Run(() => PrepareLocked(first)),
            Task.Run(() => PrepareLocked(second)));

        await Assert.That(spawns).IsEqualTo(1);
        await Assert.That(results).Contains(MentoringChestPreparationResult.Spawned);
        await Assert.That(results).Contains(MentoringChestPreparationResult.ReusedExisting);
        return;

        MentoringChestPreparationResult PrepareLocked(Npc npc) =>
            MentoringChestRestorationService.PrepareForBossDeathLocked(
                sync,
                npc,
                true,
                50,
                Trigger,
                _ => chestExists,
                _ =>
                {
                    Interlocked.Increment(ref spawns);
                    chestExists = true;
                    return new Doodad();
                });
    }

    [Test]
    public async Task CreateSpawner_UsesBossTransformAndContentPhases()
    {
        var world = CreateWorld();
        var npc = new Npc { TemplateId = 11364 };
        SetParentWorld(npc, world);
        typeof(AAEmu.Game.Models.Game.World.Transform.Transform)
            .GetField("_zoneId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(npc.Transform, 262u);
        npc.Transform.Local.SetPosition(12.5f, 23.5f, 34.5f, 0.1f, 0.2f, 0.3f);

        var spawner = MentoringChestRestorationService.CreateSpawner(npc, Trigger);

        await Assert.That(spawner.ParentWorld).IsSameReferenceAs(world);
        await Assert.That(spawner.UnitId).IsEqualTo(7522u);
        await Assert.That(spawner.FuncGroupId).IsEqualTo(20831u);
        await Assert.That(spawner.Position.ZoneId).IsEqualTo(262u);
        await Assert.That(spawner.Position.X).IsEqualTo(12.5f);
        await Assert.That(spawner.Position.Y).IsEqualTo(23.5f);
        await Assert.That(spawner.Position.Z).IsEqualTo(34.5f);
        await Assert.That(spawner.Position.Roll).IsEqualTo(0.1f);
        await Assert.That(spawner.Position.Pitch).IsEqualTo(0.2f);
        await Assert.That(spawner.Position.Yaw).IsEqualTo(0.3f);
    }

    [Test]
    public async Task RuntimeSpawner_RejectsARespawnAfterWorldDisposal()
    {
        var world = CreateWorld();
        var npc = new Npc { TemplateId = 11364 };
        SetParentWorld(npc, world);
        var spawner = MentoringChestRestorationService.CreateSpawner(npc, Trigger);
        typeof(WorldInstance)
            .GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(world, 1);

        var doodad = spawner.Spawn(0);

        await Assert.That(world.IsDisposed).IsTrue();
        await Assert.That(doodad).IsNull();
    }

    private static MentoringChestPreparationResult Prepare(
        Npc npc,
        bool isDungeon,
        uint zoneGroupId,
        bool exists,
        Action onSpawn)
    {
        return MentoringChestRestorationService.PrepareForBossDeath(
            npc,
            isDungeon,
            zoneGroupId,
            Trigger,
            _ => exists,
            _ =>
            {
                onSpawn();
                return new Doodad();
            });
    }

    private static WorldInstance CreateWorld() =>
        new(new WorldTemplate { Id = 2, Name = "mentoring-test" }, 0, true, 7000);

    private static void SetParentWorld(Npc npc, WorldInstance world) =>
        typeof(AAEmu.Game.Models.Game.World.GameObject)
            .GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(npc, world);
}
