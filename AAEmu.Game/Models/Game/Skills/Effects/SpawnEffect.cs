using System;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class SpawnEffect : EffectTemplate
    {
        public uint OwnerTypeId { get; set; }
        public uint SubType { get; set; }
        public uint PosDirId { get; set; }
        public float PosAngle { get; set; }
        public float PosDistance { get; set; }
        public uint OriDirId { get; set; }
        public float OriAngle { get; set; }
        public bool UseSummonerFaction { get; set; }
        public float LifeTime { get; set; }
        public bool DespawnOnCreatorDeath { get; set; }

        public bool UseSummonerAggroTarget { get; set; }
        // TODO 1.2 // public uint MateStateId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Trace("SpawnEffect");

            if (OwnerTypeId == 1) // NPC
            {
                var spawner = SpawnManager.Instance.GetNpcSpawner(SubType, target);
                if (spawner == null)
                {
                    return;
                }
                var (xx, yy) = MathUtil.AddDistanceToFrontDeg(PosDistance, target.Transform.World.Position.X, target.Transform.World.Position.Y, PosAngle);
                var zz = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(target.Transform.ZoneId, xx, yy) : target.Transform.World.Position.Z;
                spawner.Position.X = xx;
                spawner.Position.Y = yy;
                spawner.Position.Z = zz;
                spawner.Position.Roll = PosAngle;

                spawner.RespawnTime = 0; // don't respawn

                spawner.DoSpawnEffect(spawner.Id, this, caster, target);
            }
        }
    }
}
