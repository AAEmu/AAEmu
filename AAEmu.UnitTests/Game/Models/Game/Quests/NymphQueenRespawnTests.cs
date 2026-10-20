using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Scripts;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class NymphQueenRespawnTests
{
    [Test]
    public async Task RestoreQuestNpc_AfterScriptedDeletion_ClearsStalePopulationAndSpawnsOnce()
    {
        var world = new WorldInstance(new WorldTemplate { Name = "test" }, 0, true, uint.MaxValue);
        var spawner = new RecoverableSpawner { ParentWorld = world, SpawnerId = 10469, UnitId = 9385,
            Template = new NpcSpawnerTemplate { Id = 10469 } };
        var stale = new Npc { ObjId = 10, TemplateId = 9385, ParentWorld = world };
        spawner.SpawnedNpcs[10469] = [stale];
        spawner.SetSpawnScheduled(true);

        var restored = spawner.RestoreQuestNpc();
        var repeated = spawner.RestoreQuestNpc();

        await Assert.That(restored.TemplateId).IsEqualTo(9385u);
        await Assert.That(ReferenceEquals(restored, repeated)).IsTrue();
        await Assert.That(spawner.SpawnCalls).IsEqualTo(1);
        await Assert.That(spawner.SpawnedNpcs[10469].Count).IsEqualTo(1);
        await Assert.That(spawner.SpawnedNpcs[10469].Contains(stale)).IsFalse();
    }

    [Test]
    public async Task FindQuestNpc_StaleSelectedNpc_UsesRegisteredNpcInstead()
    {
        var world = new WorldInstance(new WorldTemplate { Name = "test" }, 0, true, uint.MaxValue);
        var player = new Character(new UnitCustomModelParams()) { ParentWorld = world };
        player.CurrentTarget = new Npc { ObjId = 10, TemplateId = 9385 };
        var actual = new Npc { ObjId = 11, TemplateId = 9385 };
        world.SetNpc(actual.ObjId, actual);

        var result = NymphQueenEncounter.FindQuestNpc(player, false);

        await Assert.That(ReferenceEquals(result, actual)).IsTrue();
    }

    private sealed class RecoverableSpawner : NpcSpawner
    {
        public int SpawnCalls { get; private set; }

        public override Npc Spawn(uint objId)
        {
            SpawnCalls++;
            var npc = new Npc { ObjId = (uint)(100 + SpawnCalls), TemplateId = UnitId, Spawner = this,
                ParentWorld = ParentWorld };
            SpawnedNpcs[SpawnerId] = [npc];
            ParentWorld.SetNpc(npc.ObjId, npc);
            return npc;
        }
    }
}
