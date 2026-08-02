using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.World;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class NpcSpawnerSpawnEffect : EffectTemplate
{
    public uint SpawnerId { get; set; }
    public float LifeTime { get; set; }
    public bool DespawnOnCreatorDeath { get; set; }
    public bool UseSummonerAggroTarget { get; set; }
    public bool ActivationState { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"NpcSpawnerSpawnEffect: SpawnerId={SpawnerId}, LifeTime={LifeTime}, UseSummonerAggroTarget={UseSummonerAggroTarget}, ActivationState={ActivationState}");

        if (WorldIntegration.ZoneAuthority)
        {
            // A spawner left active may keep producing its normal population. The one-shot form
            // tells Zone to stand it down after this skill-authored spawn.
            var spawnerEvent = ActivationState
                ? NpcSpawnerEvent.SpawnAllOnce
                : NpcSpawnerEvent.SpawnAllOnceAndDeactivate;

            if (!WorldIntegration.PublishNpcSpawnerEvent(
                    caster,
                    SpawnerId,
                    spawnerEvent,
                    LifeTime,
                    DespawnOnCreatorDeath,
                    UseSummonerAggroTarget))
            {
                Logger.Warn($"NpcSpawnerSpawnEffect: no loaded Zone accepted spawner {SpawnerId}.");
            }
            return;
        }

        var spawners = caster.ParentWorld.SpawnManager.GetNpcSpawner(SpawnerId);
        if (spawners is not { Count: not 0 })
            Logger.Info($"NpcSpawnerSpawnEffect: SpawnerId={SpawnerId} not found in spawners.");
        else
        {
            foreach (var spawner in spawners)
            {
                // spawn in the same world as for caster
                spawner.Position.WorldId = caster.Transform.WorldId;
                var npc = spawner.ForceSpawn(0);
                if (npc == null)
                    continue;

                npc.Spawner.RespawnTime = 0; // запретим респавн
                Logger.Info($"NpcSpawnerSpawnEffect: Do Spawn effect id={Id}, Npc unitId={spawner.UnitId} spawnerId={SpawnerId} worldId={caster.Transform.WorldId}");

                if (UseSummonerAggroTarget)
                {
                    if (LifeTime == 0)
                    {
                        // Mutual aggro between the summon and everything hostile around it (Q3886 / Q3887).
                        var units = WorldManager.GetAround<Npc>(npc, npc.Template.SightRangeScale * 30f);
                        if (units is not { Count: not 0 })
                            continue;

                        foreach (var n in units.Where(npc.CanAttack))
                        {
                            Logger.Info($"NpcSpawnerSpawnEffect: npc={n.TemplateId}:{n.ObjId} is hostile to npc={npc.TemplateId}:{npc.ObjId}");
                            npc.AddUnitAggro(AggroKind.Damage, n, 1);
                            n.AddUnitAggro(AggroKind.Damage, npc, 1);
                        }
                    }
                    else
                    {
                        npc.AddUnitAggro(AggroKind.Damage, target is Npc targetNpc ? targetNpc : (Unit)caster, 1);
                    }
                }

                if (LifeTime > 0)
                {
                    TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(LifeTime));
                }
            }
        }
    }
}
