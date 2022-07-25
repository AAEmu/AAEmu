using System;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class NpcSpawnerSpawnEffect : EffectTemplate
    {
        public uint SpawnerId { get; set; }
        public float LifeTime { get; set; }
        public bool DespawnOnCreatorDeath { get; set; }
        public bool UseSummonerAggroTarget { get; set; }
        public bool ActivationState { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("NpcSpawnerSpawnEffect");

            var spawners = SpawnManager.Instance.GetNpcSpawner(SpawnerId, (byte)caster.Transform.WorldId);
            foreach (var spawner in spawners)
            {
                spawner.DoSpawn();
                _log.Debug("NpcSpawnerSpawnEffect id:{0}, Npc unitId:{1} spawnerId:{2}", Id, spawner.UnitId, SpawnerId);
            }
        }
    }
}
