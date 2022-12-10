using System;
using AAEmu.Commons.Utils;
using System.Reactive;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using Unit = AAEmu.Game.Models.Game.Units.Unit;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class SpawnEffect : EffectTemplate
    {
        public uint OwnerTypeId { get; set; }
        public uint SubType { get; set; }
        public uint PosDirId { get; set; }
        //public float PosAngle { get; set; } // there is no such field in the database for version 3.0.3.0
        public float PosAngleMax { get; set; }
        public float PosAngleMin { get; set; }
        public float PosDistanceMax { get; set; }
        public float PosDistanceMin { get; set; }
        public uint OriDirId { get; set; }
        public float OriAngle { get; set; }
        public bool UseSummonerFaction { get; set; }
        public float LifeTime { get; set; }
        public bool DespawnOnCreatorDeath { get; set; }
        public bool UseSummonerAggroTarget { get; set; }
        public uint MateStateId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
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

                var PosAngle = Rand.Next(PosAngleMin, PosAngleMax);
                var PosDistance = Rand.Next(PosDistanceMin, PosDistanceMax);
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
