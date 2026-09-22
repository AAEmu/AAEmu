using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

/// <summary>
/// <c>indun_actions.detail_type = 'NpcSpawnerSpawnEffect'</c> (21 rows): <c>detail_id</c> is an
/// <c>npc_spawner_spawn_effects</c> row, the same template SkillManager loads for the skill effect. All 21
/// rows carry <c>despawn_on_creator_death</c>, <c>use_summoner_aggro_target</c>, <c>use_summoner_faction</c>
/// and <c>activation_state</c> 'f' with <c>life_time</c> 0, so the creator a zone spawn needs only routes
/// the request: any player in the copy resolves its zone host.
/// </summary>
internal class IndunActionNpcSpawner : IndunAction
{
    public override void Execute(WorldInstance worldInstance)
    {
        if (worldInstance == null)
            return;

        if (SkillManager.Instance.GetEffectTemplate(DetailId, "NpcSpawnerSpawnEffect") is not NpcSpawnerSpawnEffect effect)
        {
            Logger.Debug($"IndunActionNpcSpawner {Id}: npc_spawner_spawn_effects {DetailId} is not loaded");
            return;
        }

        var spawnerId = IndunRoundRules.ResolveSpawnerId(effect.SpawnerId, worldInstance.DungeonInstance?.Rounds.CurrentSpawnerId ?? 0);
        if (spawnerId == 0)
        {
            Logger.Debug($"IndunActionNpcSpawner {Id}: effect {DetailId} names spawner 0 and world {worldInstance.Id} has no current round spawner");
            return;
        }

        var creator = worldInstance.GetAllCharacters().FirstOrDefault(c => c is { IsOnline: true });
        if (creator == null)
        {
            Logger.Debug($"IndunActionNpcSpawner {Id}: no player in world {worldInstance.Id} to route spawner {spawnerId}");
            return;
        }

        if (WorldIntegration.ZoneAuthority)
        {
            var spawnerEvent = effect.ActivationState
                ? NpcSpawnerEvent.SpawnAllOnce
                : NpcSpawnerEvent.SpawnAllOnceAndDeactivate;
            if (!WorldIntegration.PublishNpcSpawnerEvent(creator, spawnerId, spawnerEvent, effect.LifeTime, effect.DespawnOnCreatorDeath, effect.UseSummonerAggroTarget))
                Logger.Debug($"IndunActionNpcSpawner {Id}: no zone host accepted spawner {spawnerId} for world {worldInstance.Id}");
            return;
        }

        // 19 of the 21 spawner ids and every indun_rounds spawner lie past the client's npc_spawners
        // range (max id 24894); GetNpcSpawner then returns nothing and the action logs instead of spawning.
        var spawners = worldInstance.SpawnManager?.GetNpcSpawner(spawnerId);
        if (spawners is not { Count: > 0 })
        {
            Logger.Debug($"IndunActionNpcSpawner {Id}: spawner {spawnerId} is not among world {worldInstance.Id}'s event spawners");
            return;
        }

        foreach (var spawner in spawners)
        {
            spawner.Position.WorldId = worldInstance.Id;
            var npc = spawner.ForceSpawn(0);
            if (npc == null)
                continue;

            npc.Spawner.RespawnTime = 0;
            if (effect.LifeTime > 0)
                TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(effect.LifeTime));
        }
    }
}
