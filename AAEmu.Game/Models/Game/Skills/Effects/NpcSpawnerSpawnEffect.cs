using System;
using System.Linq;

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

        var world = WorldManager.Instance.GetWorldByZone(caster.Transform.ZoneId);
        var spawners = SpawnManager.Instance.GetNpcSpawner(SpawnerId, (byte)world.TemplateId);
        if (spawners is not { Count: not 0 })
            Logger.Info($"NpcSpawnerSpawnEffect: SpawnerId={SpawnerId} not found in spawners.");
        else
        {
            foreach (var spawner in spawners)
            {
                // spawn in the same world as for caster
                spawner.Position.WorldId = caster.Transform.WorldId;
                spawner.ClearLastSpawnCount();
                var npc = spawner.ForceSpawn(0);
                if (npc == null)
                    continue;

                npc.Spawner.RespawnTime = 0; // запретим респавн
                Logger.Info($"NpcSpawnerSpawnEffect: Do Spawn effect id={Id}, Npc unitId={spawner.UnitId} spawnerId={SpawnerId} worldId={caster.Transform.WorldId}");

                if (UseSummonerAggroTarget)
                {
                    if (LifeTime == 0)
                    {
                        // Npc attacks Npc for Q3886 & Q3887
                        var units = WorldManager.GetAround<Npc>(npc, npc.Ai.Owner.Template.SightRangeScale * 30f);
                        if (units is not { Count: not 0 })
                            continue;

                        foreach (var n in units.Where(n => npc.Ai.Owner.CanAttack(n)))
                        {
                            Logger.Info($"NpcSpawnerSpawnEffect: npc={n.TemplateId}:{npc.ObjId} attack the npc={npc.TemplateId}:{npc.ObjId}");
                            npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, n, 1);
                            npc.Ai.OnAggroTargetChanged();

                            n.Ai.Owner.AddUnitAggro(AggroKind.Damage, npc, 1);
                        }
                        //npc.Ai.GoToCombat();
                    }
                    else
                    {
                        // Npc attacks the character
                        if (target is Npc)
                        {
                            npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)target, 1);
                        }
                        else
                        {
                            npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)caster, 1);
                        }

                        npc.Ai.OnAggroTargetChanged();
                        //npc.Ai.GoToCombat();
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
