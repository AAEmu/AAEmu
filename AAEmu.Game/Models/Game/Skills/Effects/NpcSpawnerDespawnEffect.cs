using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class NpcSpawnerDespawnEffect : EffectTemplate
    {
        public uint SpawnerId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("NpcSpawnerDespawnEffect");

            //var spawner = SpawnManager.Instance.GetNpcSpawner(SpawnerId, (byte)caster.Transform.WorldId);
            //spawner.DoDespawn();

            //_log.Debug("NpcSpawnerDespawnEffect id:{0}, Npc unitId:{1} spawnerId:{2}", Id, spawner.UnitId, SpawnerId);
        }
    }
}
